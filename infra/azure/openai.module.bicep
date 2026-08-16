// ============================================================================
//  The OpenAI account and its two deployments.
//
//  Two, not one, and the split is ADR-0004 made of metal: `composer` is the
//  demo's reply rewriter and `judge` is Layer 2's grader, pinned separately so
//  the judge cannot move because somebody wanted nicer demo prose. The
//  deployment NAMES are the contract — configuration everywhere else says
//  `Llm__Model=composer` and `Llm__JudgeModel=judge`, never a model id,
//  because conflating deployment names with model ids is the usual way an
//  Azure OpenAI integration fails at the first call (flyio/SECRETS.md).
//
//  Deployments are chained with dependsOn: Azure OpenAI accepts one deployment
//  operation at a time, and parallel creation fails with a conflict. Learned
//  in the estate's reference SaaS, where the comment on the same chain calls it the
//  load-bearing gotcha of the whole file.
// ============================================================================

@description('Region for the account.')
param location string

@description('Seeds the account name.')
param environmentName string

@description('True when restoring a soft-deleted account of the same name.')
param restore bool = false

@description('Custom subdomain (required for AAD flows, stable across re-provisions). Empty derives one.')
param customSubDomainName string = ''

var accountName = 'oai-${environmentName}'
var subdomain = empty(customSubDomainName)
  ? toLower('${environmentName}-${uniqueString(resourceGroup().id)}')
  : customSubDomainName

resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: subdomain
    publicNetworkAccess: 'Enabled'
    restore: restore ? true : null
  }
}

// The demo's composer: small and cheap on purpose. It rewrites one sentence
// per turn under a 300-token ceiling; a frontier model here would be spend
// with no observable difference on the page.
resource composer 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: 'composer'
  sku: {
    name: 'GlobalStandard'
    capacity: 20
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o-mini'
      version: '2024-07-18'
    }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
}

// Layer 2's judge. A stronger model than the composer because grading a
// transcript against anchored rubrics is harder than rewording a sentence —
// and pinned with NoAutoUpgrade for the same reason the judge prompt is
// hashed: a judge that silently upgrades is a measuring stick that changed
// length between readings (AI-EVALS.md §5, ADR-0004).
resource judge 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: 'judge'
  sku: {
    name: 'GlobalStandard'
    capacity: 20
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
  dependsOn: [
    composer
  ]
}

output accountName string = account.name
output endpoint string = account.properties.endpoint
output composerDeploymentName string = composer.name
output judgeDeploymentName string = judge.name
