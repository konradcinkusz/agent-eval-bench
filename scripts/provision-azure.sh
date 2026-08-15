#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  The local mirror of .github/workflows/azure.yml — the same Bicep, the same
#  outputs, runnable from a laptop with an `az login` session. Mirrors exist so
#  a workflow that misbehaves can be reproduced without pushing commits at it.
#
#  What it does:
#    1. deploys infra/azure/main.bicep (resource group, OpenAI account with the
#       `composer` and `judge` deployments, Log Analytics + App Insights);
#    2. prints every value the rest of the estate wants, and the exact commands
#       that store them — it stores nothing itself, because a script that
#       writes secrets to three systems on a keypress is how a wrong
#       subscription becomes an incident.
#
#  Usage:
#    ./scripts/provision-azure.sh [environment-name] [location]
#  Defaults: agent-eval-bench, swedencentral.
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

ENV_NAME="${1:-agent-eval-bench}"
LOCATION="${2:-swedencentral}"
DEPLOYMENT_NAME="agent-eval-bench-azure"

require() {
  if ! command -v "$1" > /dev/null 2>&1; then
    echo "error: $1 is required and not on PATH." >&2
    exit 1
  fi
}

require az
require jq

if ! az account show > /dev/null 2>&1; then
  echo "error: no Azure session. Run 'az login' first." >&2
  exit 1
fi

subscription=$(az account show --query name -o tsv)
echo "Provisioning '${ENV_NAME}' in '${LOCATION}' on subscription '${subscription}'."
echo "(Ctrl-C now if that subscription is wrong.)"
sleep 3

restore_openai=false
subdomain=""

for attempt in 1 2 3; do
  echo "Deployment attempt ${attempt}…"

  set +e
  output=$(az deployment sub create \
    --name "${DEPLOYMENT_NAME}" \
    --location "${LOCATION}" \
    --template-file "$(dirname "$0")/../infra/azure/main.bicep" \
    --parameters environmentName="${ENV_NAME}" location="${LOCATION}" \
      restoreOpenAi="${restore_openai}" openAiCustomSubDomainName="${subdomain}" \
    2>&1)
  status=$?
  set -e

  if [ "${status}" -eq 0 ]; then
    break
  fi

  echo "${output}" | tail -10

  # The two ARM errors with mechanical fixes (see azure.yml for the reasoning).
  if echo "${output}" | grep -q "FlagMustBeSetForRestore"; then
    echo "Soft-deleted OpenAI account detected — retrying with restore."
    restore_openai=true
    continue
  fi

  if echo "${output}" | grep -q "UpdatingCustomDomainNotAllowed\|CustomDomainInUse"; then
    subdomain=$(az cognitiveservices account show \
      --name "oai-${ENV_NAME}" --resource-group "rg-${ENV_NAME}" \
      --query "properties.customSubDomainName" -o tsv)
    echo "Existing account found — retrying with its subdomain '${subdomain}'."
    continue
  fi

  echo "error: deployment failed for a reason with no mechanical fix (tail above)." >&2
  exit "${status}"
done

outputs=$(az deployment sub show --name "${DEPLOYMENT_NAME}" --query properties.outputs -o json)

resource_group=$(echo "${outputs}" | jq -r '.resourceGroupName.value')
openai_name=$(echo "${outputs}" | jq -r '.openAiName.value')
endpoint=$(echo "${outputs}" | jq -r '.openAiEndpoint.value')
composer=$(echo "${outputs}" | jq -r '.composerDeploymentName.value')
judge=$(echo "${outputs}" | jq -r '.judgeDeploymentName.value')
appinsights=$(echo "${outputs}" | jq -r '.appInsightsName.value')

cat <<SUMMARY

Provisioned.

  Resource group        ${resource_group}
  OpenAI account        ${openai_name}
  Endpoint              ${endpoint}
  Composer deployment   ${composer}
  Judge deployment      ${judge}
  App Insights          ${appinsights}

The key is deliberately not printed. Fetch it into a variable when you need it:

  key=\$(az cognitiveservices account keys list \\
    --name "${openai_name}" --resource-group "${resource_group}" --query key1 -o tsv)

Wire the Fly demo (applies on its next deploy):

  flyctl secrets set --app agent-eval-bench-demo --stage \\
    Llm__Provider=AzureOpenAI \\
    "Llm__Endpoint=${endpoint}" "Llm__ApiKey=\${key}" \\
    "Llm__Model=${composer}" "Llm__JudgeModel=${judge}" \\
    "APPLICATIONINSIGHTS_CONNECTION_STRING=\$(az monitor app-insights component show \\
      -g "${resource_group}" -a "${appinsights}" --query connectionString -o tsv)"

Wire the nightly judge (evals GitHub environment):

  gh api -X PUT repos/<owner>/agent-eval-bench/environments/evals > /dev/null
  gh variable set LLM_ENDPOINT    --env evals --body "${endpoint}"
  gh variable set LLM_JUDGE_MODEL --env evals --body "${judge}"
  gh secret   set LLM_API_KEY     --env evals --body "\${key}"

Run Layer 2 against the live judge from this machine:

  EVAL_LAYER2_SCOPE=full \\
  Llm__Provider=AzureOpenAI "Llm__Endpoint=${endpoint}" "Llm__ApiKey=\${key}" \\
  "Llm__JudgeModel=${judge}" \\
  dotnet test tests/AbsenceConcierge.Evals -c Release

SUMMARY
