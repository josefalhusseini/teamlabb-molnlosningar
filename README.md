# teamlabb-molnlosningar

Slutuppgift i Administrera molnlösningar.

## Rollback

Om en ny version har gått igenom pipelinen men appen ändå inte fungerar som den ska, till exempel att endpoints returnerar fel eller att certifikat inte sparas, rullar vi tillbaka till den senaste versionen som fungerade.

### 1. Hitta tidigare versioner

Varje deploy skapar en ny revision i Container Apps. Listan visar alla revisioner, vilken som är aktiv och vilket byggnummer imagen har.

```
az containerapp revision list --name certify-api --resource-group <resursgrupp> --all -o table
```

### 2. Deploya en tidigare image

Välj byggnumret från en revision som fungerade. Container Apps skapar då en ny revision med den gamla imagen och flyttar över trafiken dit.

```
az containerapp update --name certify-api --resource-group <resursgrupp> --image certifyacrpmwst7bkkpgrc.azurecr.io/certify-api:<bygg-nummer>
```

### 3. Verifiera

Kontrollera att appen svarar och att rätt revision är aktiv.

```
curl.exe -i https://certify-api.thankfulgrass-b2132c06.swedencentral.azurecontainerapps.io/health
```

Svaret ska vara `200 OK` med `"status":"Healthy"`. Kör kommandot från steg 1 igen och kontrollera att den nya revisionen är aktiv med rätt byggnummer.