// ============================================================================
//  Where production traces land, so the production loop has something to read.
//
//  D-12's missing piece was never the scoring or the extraction — both exist —
//  but "the ingestion of exported spans, which needs a collector this
//  repository does not have". This is that collector, in managed form: the
//  service's OpenTelemetry pipeline exports to Application Insights when
//  APPLICATIONINSIGHTS_CONNECTION_STRING is present, and the scheduled scoring
//  pass (.github/workflows/production-loop.yml) queries it back out with KQL.
//
//  30-day retention and a daily cap: this is a demo's trace archive, not an
//  estate's. The cap is the same instinct as the token budget — the failure
//  that only ever shows up on a bill.
// ============================================================================

@description('Region for both resources.')
param location string

@description('Seeds resource names.')
param environmentName string

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${environmentName}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${environmentName}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    RetentionInDays: 30
    IngestionMode: 'LogAnalytics'
  }
}

output appInsightsName string = appInsights.name
output connectionString string = appInsights.properties.ConnectionString
output appId string = appInsights.properties.AppId
