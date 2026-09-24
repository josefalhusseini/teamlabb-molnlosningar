# ARCHITECTURE.md — Teknisk reflektion & Arkitektur (Certify AB)

## 1. Container Apps vs AKS
Vi har valt Azure Container Apps framför Azure Kubernetes Service (AKS) eftersom vår workload för Certify AB består av ett mikrotjänst-API med varierande och händelsestyrd trafik. Container Apps körs på en serverless Consumption-plan där Azure hanterar klusterinfrastruktur, nodskalning, ingress-routing och certifikat helt abstraherat, vilket minimerar underhåll och driftskostnader. Begränsningen med Container Apps är att vi inte har direkt tillgång till underliggande Kubernetes-primitiver som DaemonSets, anpassade CNI-nätverksinställningar eller avancerad service mesh. Vi skulle välja AKS om Certify växte till ett stort ekosystem med dussintals mikrotjänster som kräver strikt nätverksisolering, dedikerade GPU-resurser eller multi-cloud portabilitet där ett eget driftteam finns på plats.

## 2. CI/CD Pipeline
Vårt pipeline-flöde i Azure DevOps triggas automatiskt vid push till `main`-branchen. Först körs ett byggsteg (`DotNetCoreCLI@2`) som återställer NuGet-paket och kompilerar .NET 8-källkoden i Release-konfiguration. Därefter körs `AzureCLI@2` som instruerar Azure Container Registry (ACR) att bygga vår multi-stage `Dockerfile` och pusha två taggar (`BuildId` och `latest`). I det avslutande steget anropas `az containerapp update` via en service connection för att driftsätta den nybyggda imagen till vår Container App. Om ett steg (t.ex. kompileringen eller imagebygget) misslyckas, avbryts pipelinen omedelbart med röd status; den befintliga live-revisionen i Azure Container Apps fortsätter då att köra helt opåverkad, vilket garanterar noll driftstopp för användarna.

## 3. Infrastruktur som kod (IaC) & Idempotens
Vi definierar hela vår infrastruktur i Bicep istället för att klicka i Azure Portal för att säkerställa spårbarhet, versionshantering och reproducerbarhet mellan miljöer. Med Bicep kan en identisk miljö rullas ut automatiskt utan mänskliga misstag, felkonfigurerade portar eller bortglömda behörigheter. Idempotens innebär att en deployment kan köras hur många gånger som helst mot samma resursgrupp och alltid resultera i exakt samma önskade tillstånd. Om en resurs redan finns och stämmer överens med Bicep-definitionen ändrar Azure ingenting, vilket gör driftsättningar säkra och förutsägbara.

## 4. Säkerhet & Hemlighetshantering
Vi eliminerar riskerna för läckta autentiseringsuppgifter genom att använda Azure Managed Identity istället för hårdkodade lösenord eller anslutningssträngar. Vår Container App har en System-Assigned identitet som via Azure RBAC tilldelats rollen *Storage Blob Data Contributor* direkt på vårt Storage Account, och koden använder `DefaultAzureCredential` för att begära kortlivade tokens. För känsliga miljövariabler, såsom `ADMIN_API_KEY`, injiceras värdet via säkra Bicep-parametrar och pipelines utan att exponeras i git. Om en hemlig nyckel av misstag skulle hamna i git-historiken betraktas den omedelbart som komprometterad och måste roteras i Azure samt raderas ur git-historiken via t.ex. `git filter-repo`.

## 5. Ekonomi & Skalbarhet

### Kostnad vid lansering (40 kunder, ~8 000 certifikat/månad)
* **Azure Container Apps:** ~0 SEK/mån (ingår i den kostnadsfria kvoten på 180 000 vCPU-sekunder och 360 000 GiB-sekunder per månad).
* **Azure Container Registry (Basic):** ~17 SEK/mån (fast avgift på ca 0.55 SEK/dag).
* **Azure Blob Storage (Standard LRS, Hot):** ~2 SEK/mån (~8 000 JSON-filer motsvarar under 5 MB data och 8 000 skrivanrop).
* **Log Analytics Workspace:** ~0 SEK/mån (under gratiskvoten på 5 GB/månad).
* **Total månadskostnad vid lansering:** **~20–25 SEK/månad**. Intäkten från 40 kunder à 99 kr är 3 960 kr/mån, vilket ger en bruttomarginal på över 99 %.

### Kostnad om kundbasen tredubblas (120 kunder, ~24 000 certifikat/månad)
* ACR och Storage Account skalar linjärt; Storage-kostnaden ökar marginellt till ca 5 SEK/månad.
* Container Apps ryms fortfarande till stor del inom gratiskvoten vid normal belastning.
* **Total månadskostnad vid 3x:** **~25–35 SEK/månad**.

### Dyrast resurs och motivering
Azure Container Registry (ACR) på Basic SKU är vår enskilt dyraste fasta resurs i denna fas (~17 SEK/månad). Skälet är att ACR har en fast dygnsavgift oavsett om man pushar nya images eller inte, till skillnad från Container Apps och Blob Storage som är rena serverless pay-as-you-go-tjänster i vila.

### Flaskhals och hantering av viral last (10 000 anrop på en dag)
Om en certifikatlänk delas viralt på sociala medier och anropas 10 000 gånger på en dag uppstår flaskhalsen i vår Container App och antalet läsanrop mot Blob Storage. 10 000 extra läsanrop mot Blob Storage kostar dock under 0.05 SEK, och Container Apps skalar automatiskt upp antalet replicas för att möta trafiken. 

För att skydda systemet och förhindra att databasen eller containrarna överbelastas om anropen skenar till miljoner, bör en caching-mekanism (t.ex. Azure Front Door / CDN) placeras framför `/verify/{uuid}`. Eftersom ett utfärdat certifikat är oföränderligt kan verifieringssvaret cachas med HTTP-headers (`Cache-Control: public, max-age=86400`), vilket avlastar infrastrukturen helt och håller kostnaden nära noll.