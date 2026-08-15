// ============================================================================
//  Everything this repository provisions into Azure, in one file's reach.
//
//  Subscription scope: it creates the resource group and composes two modules —
//  the OpenAI account with its two model deployments (the demo's composer and
//  Layer 2's judge), and the monitoring pair (Log Analytics + Application
//  Insights) that gives production traces somewhere queryable (D-12).
//
//  Driven by .github/workflows/azure.yml in CI and scripts/provision-azure.sh
//  locally — the same deployment either way, which is what makes the workflow's
//  behaviour reproducible from a laptop when it misbehaves.
//
//  The pattern is AureliusPromptus's flyio-shared stack, deliberately: compute
//  stays on Fly, Azure supplies only the managed services a demo cannot fake —
//  a paid model and a trace sink. What is NOT copied from there is the AI
//  Foundry hub/project pair, because this repository provisions no persistent
//  agents: the agent here is a step pipeline in the service, and the only
//  things Azure holds for it are model deployments and telemetry.
// ============================================================================

targetScope = 'subscription'

@minLength(3)
@maxLength(40)
@description('Names the resource group (rg-<name>) and seeds resource names.')
param environmentName string = 'agent-eval-bench'

@description('Region for everything. Pick one where the chosen models have GlobalStandard quota.')
param location string = 'swedencentral'

@description('Pass true when re-provisioning over a soft-deleted OpenAI account (ARM error FlagMustBeSetForRestore).')
param restoreOpenAi bool = false

@description('Custom subdomain for the OpenAI endpoint. Must stay stable across re-provisions of the same account.')
param openAiCustomSubDomainName string = ''

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: {
    repository: 'agent-eval-bench'
    purpose: 'demo composer + eval judge + trace sink'
  }
}

module openAi 'openai.module.bicep' = {
  name: 'openai'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
    restore: restoreOpenAi
    customSubDomainName: openAiCustomSubDomainName
  }
}

module monitor 'monitor.module.bicep' = {
  name: 'monitor'
  scope: rg
  params: {
    location: location
    environmentName: environmentName
  }
}

output resourceGroupName string = rg.name
output openAiName string = openAi.outputs.accountName
output openAiEndpoint string = openAi.outputs.endpoint
output composerDeploymentName string = openAi.outputs.composerDeploymentName
output judgeDeploymentName string = openAi.outputs.judgeDeploymentName
output appInsightsName string = monitor.outputs.appInsightsName
output appInsightsConnectionString string = monitor.outputs.connectionString
output appInsightsAppId string = monitor.outputs.appId
