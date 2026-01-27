param frontDoorProfileName string
param endpointName string
param originHostName string

resource profile 'Microsoft.Cdn/profiles@2021-06-01' = {
  name: frontDoorProfileName
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
}

resource endpoint 'Microsoft.Cdn/profiles/afdEndpoints@2021-06-01' = {
  parent: profile
  name: endpointName
  location: 'global'
  properties: {
    enabledState: 'Enabled'
  }
}

resource originGroup 'Microsoft.Cdn/profiles/originGroups@2021-06-01' = {
  parent: profile
  name: 'default-origin-group'
  properties: {
    loadBalancingSettings: {
      sampleSize: 4
      successfulSamplesRequired: 3
    }
    healthProbeSettings: {
      probePath: '/health'
      probeRequestType: 'HEAD'
      probeProtocol: 'Http'
      probeIntervalInSeconds: 100
    }
  }
}

resource origin 'Microsoft.Cdn/profiles/originGroups/origins@2021-06-01' = {
  parent: originGroup
  name: 'default-origin'
  properties: {
    hostName: originHostName
    httpPort: 80
    httpsPort: 443
    originHostHeader: originHostName
    priority: 1
    weight: 1000
    enabledState: 'Enabled'
  }
}

resource route 'Microsoft.Cdn/profiles/afdEndpoints/routes@2021-06-01' = {
  parent: endpoint
  name: 'default-route'
  properties: {
    customDomains: []
    originGroup: {
      id: originGroup.id
    }
    supportedProtocols: [
      'Http'
      'Https'
    ]
    patternsToMatch: [
      '/*'
    ]
    forwardingProtocol: 'MatchRequest'
    linkToDefaultDomain: 'Enabled'
    httpsRedirect: 'Enabled'
  }
}

// Basic WAF Policy
resource wafPolicy 'Microsoft.Network/FrontDoorWebApplicationFirewallPolicies@2022-05-01' = {
  name: 'waf${uniqueString(resourceGroup().id)}'
  location: 'global'
  sku: {
    name: 'Standard_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      mode: 'Prevention'
      requestBodyCheck: 'Enabled'
      enabledState: 'Enabled'
    }
    customRules: {
      rules: [] // Add custom rules here
    }
    managedRules: {
      managedRuleSets: [
        {
          ruleSetType: 'Microsoft_DefaultRuleSet'
          ruleSetVersion: '2.1'
          ruleGroupOverrides: []
          action: 'Block' // Default action
        }
      ]
    }
  }
}

// Associate WAF with Security Policy
resource securityPolicy 'Microsoft.Cdn/profiles/securityPolicies@2021-06-01' = {
  parent: profile
  name: 'default-security-policy'
  properties: {
    parameters: {
      type: 'WebApplicationFirewall'
      wafPolicy: {
        id: wafPolicy.id
      }
      associations: [
        {
          domains: [
             {
               id: endpoint.id
             }
          ]
          patternsToMatch: [
            '/*'
          ]
        }
      ]
    }
  }
}

output frontDoorEndpointHostName string = endpoint.properties.hostName
