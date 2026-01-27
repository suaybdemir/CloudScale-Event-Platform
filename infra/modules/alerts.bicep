// Azure Monitor Alert Rules for CloudScale Platform
// Deploy with: az deployment group create -g <rg> -f infra/modules/alerts.bicep

@description('Location for resources')
param location string = resourceGroup().location

@description('Application Insights resource ID')
param appInsightsId string

@description('Action Group ID for notifications')
param actionGroupId string = ''

@description('Environment name')
param environmentName string = 'prod'

// ============================================
// CRITICAL ALERTS (P0) - PagerDuty
// ============================================

resource highErrorRateAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-high-error-rate-${environmentName}'
  location: 'global'
  properties: {
    description: 'CRITICAL: Error rate exceeded 5% for 5 minutes'
    severity: 0
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HighErrorRate'
          metricName: 'requests/failed'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: actionGroupId != '' ? [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P0'
          runbook: 'https://wiki.internal/runbooks/high-error-rate'
        }
      }
    ] : []
  }
}

resource serviceDownAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-service-down-${environmentName}'
  location: 'global'
  properties: {
    description: 'CRITICAL: No successful requests for 2 minutes'
    severity: 0
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT2M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'NoRequests'
          metricName: 'requests/count'
          metricNamespace: 'microsoft.insights/components'
          operator: 'LessThanOrEqual'
          threshold: 0
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: actionGroupId != '' ? [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P0'
          runbook: 'https://wiki.internal/runbooks/service-down'
        }
      }
    ] : []
  }
}

// ============================================
// WARNING ALERTS (P1) - Slack
// ============================================

resource highLatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-high-latency-${environmentName}'
  location: 'global'
  properties: {
    description: 'WARNING: p99 latency exceeded 500ms for 10 minutes'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HighLatency'
          metricName: 'requests/duration'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 500
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: actionGroupId != '' ? [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P1'
          channel: '#alerts'
        }
      }
    ] : []
  }
}

resource rateLimitSpikeAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-rate-limit-spike-${environmentName}'
  location: 'global'
  properties: {
    description: 'WARNING: Rate limit rejections exceeded 100/min'
    severity: 2
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'RateLimitSpike'
          metricName: 'customMetrics/cloudscale_rate_limit_rejections_total'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 100
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: actionGroupId != '' ? [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P1'
          channel: '#capacity'
        }
      }
    ] : []
  }
}

resource fraudSpikeAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-fraud-spike-${environmentName}'
  location: 'global'
  properties: {
    description: 'SECURITY: Fraud detections exceeded 50/min'
    severity: 1
    enabled: true
    scopes: [appInsightsId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'FraudSpike'
          metricName: 'customMetrics/cloudscale_fraud_detected_total'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 50
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: actionGroupId != '' ? [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P1'
          channel: '#security'
        }
      }
    ] : []
  }
}

// ============================================
// ACTION GROUP (Notification Targets)
// ============================================

resource alertActionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-cloudscale-${environmentName}'
  location: 'global'
  properties: {
    groupShortName: 'CloudScale'
    enabled: true
    emailReceivers: [
      {
        name: 'SRE Team'
        emailAddress: 'sre-team@company.com'
        useCommonAlertSchema: true
      }
    ]
    // Uncomment and configure for production
    // webhookReceivers: [
    //   {
    //     name: 'PagerDuty'
    //     serviceUri: 'https://events.pagerduty.com/integration/xxx/enqueue'
    //     useCommonAlertSchema: true
    //   }
    //   {
    //     name: 'Slack'
    //     serviceUri: 'https://hooks.slack.com/services/xxx'
    //     useCommonAlertSchema: false
    //   }
    // ]
  }
}

output actionGroupId string = alertActionGroup.id
