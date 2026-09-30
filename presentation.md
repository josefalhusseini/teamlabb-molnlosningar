---
marp: true
theme: default
paginate: true
size: 16:9
style: |
  section { font-size: 28px; }
  h1 { color: #1f4e79; }
  h2 { color: #1f4e79; }
  table { font-size: 22px; }
  code { font-size: 20px; }
---

<!-- _class: lead -->
<!-- _paginate: false -->

# Certify AB

### Verifierbara certifikat som tjänst i Azure

Hilal Özkan och Josef Alhusseini
Teamlabb, Administrera molnlösningar

<!--
Talare: Hilal. Kort presentation av teamet och scenariot. 15 sekunder.
-->

---

## Problemet

- HR och utbildningsbolag delar ut kursintyg som Word-dokument
- Vem som helst kan ändra namnet och skriva ut
- **Ingen kan kontrollera om ett intyg är äkta**

## Vår lösning

- Ett API som skapar certifikat med en **unik verifieringslänk**
- En arbetsgivare öppnar länken och ser direkt om intyget är äkta
- Ingen inloggning krävs för att verifiera

<!--
Talare: Josef. Vad vi byggde och varför. Ungefär 1 minut.
-->

---

## API:t

| Endpoint | Vad den gör | Åtkomst |
|---|---|---|
| `POST /certificates` | Skapar certifikat, returnerar id och länk | Öppen |
| `GET /certificates/{id}` | Hämtar ett certifikat | Öppen |
| `GET /verify/{uuid}` | Publik verifiering | Öppen |
| `GET /certificates` | Listar alla | Kräver API-nyckel |
| `GET /health` | Visar att appen lever | Öppen |

Dokumenterat i Swagger på `/swagger`

<!--
Talare: Josef. Gå igenom endpoints kort, betona att /verify är produkten. Ungefär 1 minut.
-->

---

## Arkitektur

```
 git push
    │
    ▼
 GitHub ──► Azure DevOps pipeline
             1. Bygg   2. Test   3. az acr build   4. Deploy
                                      │                 │
                                      ▼                 ▼
                                    ACR  ───pull───►  Container App (2 till 5 repliker)
                                                        │         │
                                                        │         └──► Log Analytics
                                                        ▼
                                                   Blob Storage (certifikat som JSON)
```

All infrastruktur definieras i **Bicep**, `infra/main.bicep`

<!--
Talare: Hilal. Följ pilarna uppifrån och ner. Förklara att infrastrukturen deployas med Bicep och appen med pipelinen. Ungefär 1 minut.
-->

---

## Säkerhet: inga lösenord någonstans

| Vem | Pratar med | Hur |
|---|---|---|
| Container App | ACR och Blob Storage | **Managed Identity** + RBAC-roller |
| Pipeline | Azure | Service connection med **workload identity federation** |
| API-nyckel | `GET /certificates` | `@secure()` Bicep-parameter, aldrig i git |

- Blobbarna är **privata**, verifiering går alltid via API:t
- TLS 1.2 minimum, endast HTTPS

<!--
Talare: Hilal. Poängen: appen har ett id-kort, inte ett lösenord. Nämn att privata blobbar var ett medvetet val framför publika länkar. Ungefär 1 minut.
-->

---

<!-- _class: lead -->

# Live-demo

push → pipeline → live API-anrop

<!--
Talare: Hilal visar push och pipeline, Josef gör API-anropen i Swagger. 3 minuter.
Backupvideo ligger redo om något strular.
-->

---

## Demo: steg för steg

1. Liten ändring i koden, `git push` till main
2. Pipelinen startar av sig själv i Azure DevOps
3. Bygg, test, `az acr build`, deploy till Container Apps
4. Ny revision blir aktiv
5. `POST /certificates` i Swagger skapar ett certifikat
6. Öppna verifieringslänken, `isValid: true`

`https://certify-api.thankfulgrass-b2132c06.swedencentral.azurecontainerapps.io/swagger`

<!--
Talare: båda. Visa denna slide under tiden pipelinen kör, den tar runt 2 minuter.
-->

---

## Ekonomi: månadskostnad vid lansering

40 kunder, 8 000 certifikat per månad, Sweden Central

| Resurs | Kr/mån |
|---|---|
| Container Apps, 2 repliker dygnet runt | ≈ 319 |
| Container Registry, Basic | 47,58 |
| Blob Storage | ≈ 1 |
| Log Analytics | 0 |
| **Totalt** | **≈ 367** |

Intäkt 40 × 99 kr = **3 960 kr**. Infrastrukturen är runt 9 procent.

<!--
Talare: Hilal. Container Apps är dyrast för att två kopior alltid körs. Räknat med aktivt pris, alltså en övre gräns. Ungefär 1 minut.
-->

---

## Skalning: vad händer om länken går viralt?

- 10 000 delningar på en dag: **≈ 0,04 kr** i blob-läsningar
- Under 2 miljoner anrop per månad är gratis
- Autoskalning: **2 till 5 repliker** vid fler än 10 samtidiga anrop
- 3 gånger fler kunder: **≈ 370 kr**, nästan samma

**Flaskhalsar:** taket på 5 repliker, och `GET /certificates` som hämtar varje blob en och en

**Åtgärd:** cacha verifieringssvaret, ett certifikat ändras aldrig

<!--
Talare: Hilal. Svara på grundarens fråga rakt: det kostar nästan inget, och vid miljoner löser cache det. Ungefär 1 minut.
-->

---

## Drift och designval

- **Autoskalning:** HTTP-regel, `concurrentRequests: 10`, max 5 repliker
- **Parametriserad Bicep:** `main.dev.bicepparam` 1 till 2 repliker, `main.prod.bicepparam` 2 till 5
- **Rollback:** varje deploy blir en revision, tillbaka med `az containerapp update --image ...:45`

| Vi valde | Framför | Varför |
|---|---|---|
| Container Apps | AKS | Ett API, inget driftteam |
| Privata blobbar | Publika länkar | Kan logga och spärra certifikat |
| `az acr build` | Docker-task | Samma connection, ingen Docker på agenten |

<!--
Talare: Hilal. Visa gärna revisionslistan från terminalen om tid finns.
-->

---

## Vad gick fel och hur vi löste det

### 1. Deploy fick `UNAUTHORIZED` mot ACR

**Appen hade behörigheten, men visste inte att den skulle använda den.**

Lösning: `registries`-block i Bicep med `identity: 'system'`, så appen visar sin Managed Identity mot ACR

### 2. Verifieringslänken fick `http://`

**Azure avkrypterar trafiken innan den når appen, så appen visste inte att den var krypterad.**

Lösning: vi tvingar `https://` när länken byggs i koden

<!--
Talare: Hilal tar punkt 1, Josef tar punkt 2. Ungefär 1 minut.
-->

---

## Nästa steg

- Riktiga tester i pipelinen, test-steget finns men testar inget än
- Cache framför `/verify/{uuid}`
- Application Insights med larm vid fel
- Autentisering per kund i stället för en gemensam API-nyckel

<!--
Talare: Josef. Ärligt om det som saknas. 20 sekunder.
-->

---

<!-- _class: lead -->

# Frågor?

Repo: `github.com/josefalhusseini/teamlabb-molnlosningar`

<!--
2 minuter frågor från Marcus.
-->