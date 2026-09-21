param location string = resourceGroup().location

param namePrefix string = 'certify'

var uniqueSuffix = uniqueString(resourceGroup().id)

var acrName = '${namePrefix}acr${uniqueSuffix}'

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

output acrLoginServer string = acr.properties.loginServer
