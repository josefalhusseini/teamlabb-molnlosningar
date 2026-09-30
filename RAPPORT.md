# Teknisk leveransrapport

**Uppdrag:** Certify AB, verifierbara digitala certifikat som tjänst
**Konsultteam:** Josef Alhusseini och Hilal Özkan
**Datum:** 2026-10-02
**Version:** 1.0

---

## Sammanfattning

Vi har levererat en molnbaserad tjänst där Certify AB kan utfärda digitala kursintyg som går att kontrollera. Varje intyg får en unik länk, och den som öppnar länken ser direkt om intyget är äkta, utan att behöva logga in. Tidigare kunde vem som helst ändra namnet i ett Word-dokument och skriva ut det. Nu finns en spårbar och verifierbar källa för varje intyg. Tjänsten körs i Microsoft Azure och skalar automatiskt vid ökad trafik.

---

## Vad som levereras

### Inkluderat i leveransen

| Komponent | Teknisk lösning | Status |
|-----------|----------------|--------|
| REST API | .NET 8 Web API, 4 affärsendpoints och health check | ✅ Levererat |
| Publik verifiering | `GET /verify/{uuid}` via API:t | ✅ Levererat |
| Containerisering | Docker, multi-stage build | ✅ Levererat |
| Driftsättning | Azure Container Apps, 2 till 5 repliker | ✅ Levererat |
| Bildarkiv | Azure Container Registry, Basic | ✅ Levererat |
| Certifikatlagring | Azure Blob Storage, ett JSON-dokument per certifikat | ✅ Levererat |
| Autentisering och säkerhet | Systemtilldelad Managed Identity med RBAC | ✅ Levererat |
| Infrastruktur som kod | Bicep, med parameterfiler för dev och prod | ✅ Levererat |
| Automatiserad driftsättning | Azure DevOps YAML-pipeline | ✅ Levererat |
| Automatiska tester | 2 integrationstester för `/health` och `/verify`, körs i pipelinen | ✅ Levererat |
| Autoskalning | HTTP-regel, skalar upp vid fler än 10 samtidiga anrop | ✅ Levererat |
| Rollback | Dokumenterad i `README.md` och demonstrerad | ✅ Levererat |
| Loggning | Log Analytics workspace | ✅ Levererat |
| API-dokumentation | OpenAPI, Swagger UI på `/swagger` | ✅ Levererat |

### Utanför leveransens scope

Följande punkter identifierades under uppdraget men ingår inte i denna leverans. De rekommenderas som nästa steg.

| Punkt | Motivering |
|-------|-----------|
| PDF-rendering | Enligt rekommendationen för scenario D returneras certifikaten som strukturerad JSON. Det ger samma verifieringsvärde och är snabbare och stabilare i en container än PDF-generering, som kräver externa bibliotek. |
| Autentisering per kund | Idag delar alla en gemensam API-nyckel för att lista certifikat. Varje kund bör få egen åtkomst innan lansering. |
| Utökad testtäckning | Två integrationstester körs i minnesläge. Fler tester behövs, bland annat mot lagringen med Azurite, innan fler utvecklare arbetar i koden. |
| Cache framför verifiering | Behövs inte vid dagens trafik, men blir viktigt om enskilda certifikat får mycket stor spridning. |
| Monitoring med larm | Loggar samlas in, men Application Insights och larm vid fel är inte uppsatta. |
| Separat dev-miljö | Parameterfil för dev finns, men dev bör ligga i en egen resursgrupp. Kontot tillät inte nya resursgrupper. |
| Disaster recovery-plan | Utanför tidsramen för denna sprint, bör definieras innan produktionssättning. |

---

## Arkitektur

### Systemdiagram

```text
[Klient / Webbläsare]
        │
        ├──► GET  /health             (200 OK)
        ├──► GET  /swagger            (OpenAPI UI)
        ├──► POST /certificates       (Skapa certifikat)
        ├──► GET  /certificates/{id}  (Hämta certifikat)
        ├──► GET  /certificates       (Lista alla, kräver X-Api-Key)
        ├──► GET  /verify/{uuid}      (Publik verifiering)
        │
        ▼ HTTPS
[Azure Container Apps: certify-api, 2 till 5 repliker] ──► [Log Analytics]
        │
        │ Managed Identity (DefaultAzureCredential)
        ▼ RBAC: Storage Blob Data Contributor
[Azure Blob Storage: certificates, privat]


[git push] ──► [Azure DevOps pipeline] ──► [Azure Container Registry]
                                                     │
                                  AcrPull via Managed Identity
                                                     ▼
                                        [ny revision i Container Apps]
```

### Motiverade arkitekturval

**Varför Azure Container Apps och inte AKS?**
Certify är ett nystartat bolag med ett enda API och inget eget driftteam. Container Apps sköter servrar, nätverk, certifikat och skalning automatiskt, så bolaget betalar bara för det som körs och behöver ingen som underhåller ett kluster. AKS ger mer kontroll, till exempel över hur trafiken krypteras hela vägen in till applikationen, men den kontrollen kräver kompetens som bolaget inte har i denna fas. AKS blir aktuellt först om Certify växer till många tjänster eller får krav som Container Apps inte kan uppfylla.

**Varför Bicep och inte manuell konfiguration?**
Hela infrastrukturen finns beskriven i filen `infra/main.bicep`, så den kan återskapas exakt likadant i en ny miljö. Alla ändringar syns i versionshistoriken och kan granskas innan de genomförs. Mallen är idempotent, vilket betyder att den kan köras flera gånger utan att något dubbleras eller går sönder.

**Varför Azure Blob Storage för certifikatlagring?**
Ett certifikat är ett litet dokument på några hundra byte, och 8 000 certifikat i månaden kostar under en krona att lagra. Blob Storage kräver ingen databas att underhålla och integreras direkt med Managed Identity, så applikationen når lagringen utan lösenord. Lagringen är privat, och all verifiering går via API:t, vilket gör att vi kan logga, kontrollera och vid behov spärra certifikat.

**Varför Managed Identity och inte API-nycklar eller connection strings?**
Container Appen får en egen identitet i Azure, och den identiteten får behörighet till lagringen och registret via roller. Applikationen hämtar kortlivade tokens automatiskt med `DefaultAzureCredential`, så det finns ingen nyckel som kan läcka i källkod, pipelines eller loggar, och ingen nyckel som behöver bytas ut regelbundet.

---

## Säkerhetsarkitektur

### Identitet och åtkomst

| Resurs | Åtkomstkontroll |
|--------|----------------|
| Azure Container Apps | Systemtilldelad Managed Identity, ingen hårdkodad nyckel |
| Azure Blob Storage | RBAC via Managed Identity (Storage Blob Data Contributor), publik åtkomst avstängd, TLS 1.2 |
| Azure Container Registry | RBAC via Managed Identity (AcrPull), admin-användare avstängd |
| Pipeline-credentials | Azure DevOps service connection med workload identity federation, inget sparat lösenord |
| Administrativa API-anrop | API-nyckel i headern `X-Api-Key`, skickas in som säker Bicep-parameter (`@secure()`) |

### Hemlighetshantering

Applikationen har inga hemligheter för att nå Azure-tjänster, eftersom den använder Managed Identity mot både lagring och registret. Pipelinen loggar in med en kortlivad token per körning i stället för ett sparat lösenord. Den enda hemligheten, API-nyckeln, skickas in som säker parameter vid driftsättning och läses från en miljövariabel, så värdet varken finns i källkoden, i git eller i deployment-historiken.

### Kvarvarande risker

| Risk | Sannolikhet | Åtgärd |
|------|------------|--------|
| Alla kunder delar samma API-nyckel för att lista certifikat | Hög | Inför åtkomst per kund, till exempel via Entra ID |
| Ingen begränsning av antal anrop på `POST /certificates` | Medel | Lägg till API Management eller throttling |
| `GET /certificates` hämtar varje certifikat separat, blir långsamt med många certifikat | Medel | Returnera endast id:n eller inför paginering |
| En tidigare testnyckel finns kvar i git-historiken | Låg | Nyckeln används inte i produktion, produktionsnyckeln är en annan |
| LRS lagrar all data i ett datacenter | Låg | Byt till ZRS om certifikaten blir affärskritiska |

---

## Kostnadskalkyl

### Månadskostnad vid lansering

| Resurs | SKU | Uppskattad kostnad/mån |
|--------|-----|----------------------|
| Container Apps Environment | Consumption | 0 kr |
| Container App | Consumption, 2 repliker × 0,25 vCPU, 0,5 GiB | ≈ 319 kr |
| Azure Container Registry | Basic | 47,58 kr |
| Azure Blob Storage | Standard LRS, Hot | ≈ 1 kr |
| Log Analytics | PerGB2018, under 5 GB gratis | 0 kr |
| Azure DevOps | Basic | Gratis |
| **Totalt** | | **≈ 367 kr/mån** |

> Beräknat med 40 kunder och 8 000 certifikat per månad, region Sweden Central. Källa: Azure Pricing Calculator. Container Apps är beräknat med aktivt pris och är därför en övre gräns, repliker som inte hanterar anrop debiteras ett lägre vilopris. Om prenumerationens gratiskvot redan är förbrukad blir totalen cirka 419 kr/mån.

Intäkten vid lansering är 40 × 99 kr = 3 960 kr per månad, så infrastrukturen motsvarar ungefär 9 procent. Om kundbasen tredubblas blir kostnaden cirka 370 kr, eftersom den styrs av att två repliker alltid körs och inte av antalet kunder.

### Skalningspunkt

Den största kostnaden är de två repliker som alltid körs, och den ändras knappt med trafiken. Om en verifieringslänk delas 10 000 gånger på en dag kostar det runt 0,04 kr i läsningar mot lagringen, och anropen ryms inom de två miljoner gratis anropen per månad. Vid ökad trafik skalar appen automatiskt upp till fem repliker, där varje extra aktiv replik kostar runt 0,26 kr per timme. Flaskhalsarna är taket på fem repliker och endpointen som listar alla certifikat, som blir långsammare ju fler certifikat som finns. Eftersom ett certifikat aldrig ändras efter att det utfärdats kan verifieringssvaret cachas, till exempel med `Cache-Control` eller Azure Front Door, vilket avlastar både applikationen och lagringen.

---

## Rekommendationer inför produktionssättning

1. **Autentisering per kund:** ersätt den gemensamma API-nyckeln med OAuth2 eller OIDC via Entra ID inför lansering med många kunder.
2. **Edge caching:** placera Azure Front Door eller CDN framför `/verify/{uuid}` för att hantera virala toppar utan att belasta backend.
3. **Monitoring:** koppla Application Insights och sätt upp larm vid fel över 2 procent eller svarstid över 2 sekunder.
4. **Utökad testtäckning:** bygg ut testprojektet med tester för alla endpoints och mot lagringen, så att fler fel stoppas innan de når produktion.
5. **Kostnadslarm:** sätt budget-alert i Azure Cost Management vid 80 procent av månadsbudgeten.
6. **Separata miljöer:** lägg dev och prod i egna resursgrupper och deploya med respektive parameterfil.

---

## Överlämning

| Leverabel | Plats |
|-----------|-------|
| Källkod | `https://github.com/josefalhusseini/teamlabb-molnlosningar` |
| Bicep-mallar | `/infra/` i repot |
| Pipeline-definition | `azure-pipelines.yml` i repots rot |
| Rollback-instruktion | `README.md` i repots rot |
| API-dokumentation | `https://certify-api.thankfulgrass-b2132c06.swedencentral.azurecontainerapps.io/swagger` |
| Denna rapport | `RAPPORT.md` i repots rot |

---

*Rapporten är upprättad av konsultteamet som ett avslutande leveransdokument. Frågor hänvisas till teamet via Azure DevOps.*