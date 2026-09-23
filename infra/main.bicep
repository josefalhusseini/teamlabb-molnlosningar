param location string = resourceGroup().location

param namePrefix string = 'certify'

param containerImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

var uniqueSuffix = uniqueString(resourceGroup().id)

var acrName = '${namePrefix}acr${uniqueSuffix}'

var storageName = 'st${uniqueSuffix}'

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
    name: acrName
    location: location
    sku: {
        name:'Basic'
    }
    properties: {
        adminUserEnabled: false
    }
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
    name: storageName
    location: location
    sku: {
        name: 'Standard_LRS'
    }
    kind: 'StorageV2'
    properties: {
        allowBlobPublicAccess: false
        minimumTlsVersion: 'TLS1_2'
        supportsHttpsTrafficOnly: true
    }

    resource blobService 'blobServices' = {
        name: 'default'

        resource certificatesContainer 'containers' = {
            name: 'certificates'
            properties: {
                publicAccess: 'None'
            }
        }
    }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
    name: '${namePrefix}-logs'
    location: location
    properties: {
        sku: {
            name: 'PerGB2018'
        }
        retentionInDays: 30
    }
}


resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
    name: '${namePrefix}-env'
    location: location
    properties: {
        appLogsConfiguration: {
            destination: 'log-analytics'
            logAnalyticsConfiguration: {
                customerId: logAnalytics.properties.customerId
                sharedKey: logAnalytics.listKeys().primarySharedKey
            }
        }
    }
}


resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
    name: '${namePrefix}-api'
    location: location
    identity: {
        type: 'SystemAssigned'
    }
    properties: {
        managedEnvironmentId: containerEnv.id
        configuration: {
            ingress: {
                external: true
                targetPort: 80
                transport: 'auto'
            }
        }
        template: {
            containers: [
                {
                    name: 'api'
                    image: containerImage
                    resources: {
                        cpu: json('0.25')
                        memory: '0.5Gi'
                    }
                }
            ]
            scale: {
                minReplicas: 2
            }
            
            
        }
    }
}






output acrLoginServer string = acr.properties.loginServer

output logAnalyticsWorkspaceId string = logAnalytics.id

output containerAppUrl string = containerApp.properties.configuration.ingress.fqdn
