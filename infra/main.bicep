@description('Environment tag applied to all resources (e.g. dev, pro)')
param environmentName string

@description('Azure region')
param location string = resourceGroup().location

@description('App Service Plan SKU (e.g. F1, B1)')
param planSku string

@description('Web app name (must be globally unique)')
param webAppName string

@description('Key Vault name (must be globally unique, 3-24 chars)')
param vaultName string

@description('ASPNETCORE_ENVIRONMENT value - selects which appsettings.{Environment}.json the app layers in')
param aspNetCoreEnvironment string

var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${webAppName}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: planSku
  }
  properties: {
    reserved: true
  }
  tags: {
    environment: environmentName
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'KeyVaultUri'
          value: vault.properties.vaultUri
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspNetCoreEnvironment
        }
        // Runs the app from the deployed zip, mounted read-only, so a deploy swaps the whole package instead of overwriting DLLs the
        // running process has open (which surfaced as BadImageFormatException on requests that landed mid-deploy).
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ]
    }
  }
  tags: {
    environment: environmentName
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
  }
  tags: {
    environment: environmentName
  }
}

// Lets the web app's own managed identity read secrets via Key Vault references.
resource webAppKeyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, webApp.id, 'KeyVaultSecretsUser')
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = webApp.name
output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
