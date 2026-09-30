using 'main.bicep'

param minReplicas = 1
param maxReplicas = 2
param containerImage = 'certifyacrpmwst7bkkpgrc.azurecr.io/certify-api:latest'
param adminApiKey = readEnvironmentVariable('ADMIN_API_KEY')
