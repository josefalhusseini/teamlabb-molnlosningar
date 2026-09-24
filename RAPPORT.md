# Teknisk leveransrapport

**Uppdrag:** Certify AB — Verifierbara digitala certifikat som tjänst  
**Konsultteam:** Josef Alhusseini & Hilal Özkan  
**Datum:** 2026-09-25  
**Version:** 1.0  

---

## Sammanfattning
Vi har framgångsrikt designat, byggt och driftsatt en molnbaserad SaaS-plattform för digital certifikathantering åt Certify AB. Kunden kan nu via ett säkert REST-API automatiskt generera verifierbara certifikat, lagra certifikatdata säkert i Azure Blob Storage och tillhandahålla en publik verifieringslänk för slutanvändare. Hela lösningen är automatiserad med infrastruktur som kod (Bicep) och driftsatt i en serverless containerarkitektur på Microsoft Azure via en automatisk CI/CD-pipeline i Azure DevOps.

---

## Vad som levereras

### Inkluderat i leveransen

| Komponent | Teknisk lösning | Status |
|---|---|---|
| REST API | .NET 8 Web API (4 affärsendpoints + health check) | Levererat |
| Containerisering | Multi-stage Dockerfile (optimerad för .NET 8) | Levererat |
| Driftsättning | Azure Container Apps (serverless Consumption, min 2 replicas) | Levererat |
| Bildarkiv | Azure Container Registry (Basic SKU) | Levererat |
| Fillagring | Azure Blob Storage (Standard LRS) | Levererat |
| Autentisering & Säkerhet | Azure Managed Identity (System-Assigned RBAC) | Levererat |
| Infrastruktur som kod | Bicep (`main.bicep` med parameterstyrning) | Levererat |
| CI/CD Pipeline | Azure DevOps YAML-pipeline med automatisk driftsättning | Levererat |
| API-dokumentation | OpenAPI / Swagger UI på `/swagger` | Levererat |

### Utanför leveransens scope

| Punkt | Motivering |
|---|---|
| PDF-rendering i backend | Marcus rekommendation för Scenario D följdes: strukturerad JSON levererar samma värde och är snabbare och stabilare i en container. |
| Global CDN-caching (Azure Front Door) | Rekommenderas som nästa steg inför fullskalig publik marknadslansering. |

---

## Arkitektur

### Systemdiagram

```text
[Klient / Webbläsare]
        │
        ├──► GET /health  (200 OK)
        ├──► GET /swagger (OpenAPI UI)
        ├──► POST /certificates (Skapa certifikat)
        ├──► GET /certificates/{id} (Hämta certifikat)
        ├──► GET /verify/{uuid} (Publik verifiering)
        │
        ▼ (HTTPS)
[Azure Container Apps: certify-api (2 Replicas)]
        │
        ├──► Autentiserar via Managed Identity (DefaultAzureCredential)
        │
        ▼ (RBAC: Storage Blob Data Contributor)
[Azure Blob Storage: stpmwst7bkkpgrc / certificates]
```

### Motiverade arkitekturval

* **Azure Container Apps istället för AKS:** Container Apps eliminerar behovet av att hantera klusternoder, nätverksplugin och Kubernetes-overhead, vilket håller driftkostnaden på nära noll i viloläge och möjliggör snabb utveckling.
* **Bicep istället för Azure Portal:** Garanterar full reproducerbarhet, idempotens och versionshanterad infrastruktur utan risk för manuella konfigurationsfel.
* **Managed Identity istället för API-nycklar/ConnectionString:** Genom att ge containerns identitet behörighet via RBAC undviks alla risker för läckta hemligheter i källkod och pipelines.

---

## Säkerhetsarkitektur

### Identitet och åtkomst

| Resurs | Åtkomstkontroll |
|---|---|
| Azure Container Apps | System-Assigned Managed Identity |
| Azure Blob Storage | RBAC: *Storage Blob Data Contributor* tilldelad containerns identitet |
| Azure Container Registry | RBAC: *AcrPull* tilldelad containerns identitet |
| Administrativa API-anrop | API-nyckel valideras via HTTP Header (`X-Api-Key`) |

### Hemlighetshantering
Inga hemligheter eller lösenord lagras i källkod eller git-historik. Känsliga variabler injiceras dynamiskt vid drift via Azure Container Apps miljövariabler och säkra Bicep-parametrar.

---

## Kostnadskalkyl

### Månadskostnad vid lansering (40 kunder, ~8 000 certifikat/månad)

| Resurs | SKU | Uppskattad kostnad/mån |
|---|---|---|
| Container Apps Environment | Consumption | 0 SEK |
| Container App (2 replicas) | Consumption | ~0–5 SEK (ryms inom gratiskvoten) |
| Azure Container Registry | Basic | ~17 SEK |
| Azure Blob Storage | Standard LRS | ~2 SEK |
| Log Analytics Workspace | PerGB2018 | 0 SEK (under 5 GB/mån) |
| **Totalt** | | **~20–25 SEK/mån** |

---

## Rekommendationer inför nästa fas
1. **Införande av Edge Caching (Azure Front Door / CDN):** För att hantera virala toppar på `/verify/{uuid}` utan att belasta backend.
2. **Entra ID-autentisering för kunder:** Ersätta enkel administratörs-API-nyckel med OAuth2/OIDC inför multi-tenant lansering.
3. **Application Insights Alerts:** Sätta upp automatiska varningar vid felkvoter överstigande 2 %.
