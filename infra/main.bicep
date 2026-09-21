param location string = resourceGroup().location

param namePrefix string = 'certify'

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
}


output acrLoginServer string = acr.properties.loginServer
