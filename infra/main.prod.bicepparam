using 'main.bicep'

param minReplicas = 2
param maxReplicas = 5
param containerImage = 'certifyacrpmwst7bkkpgrc.azurecr.io/certify-api:latest'
param adminApiKey = readEnvironmentVariable('ADMIN_API_KEY')
