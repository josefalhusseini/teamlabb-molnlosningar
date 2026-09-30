# ARCHITECTURE.md

Teknisk reflektion och arkitektur för Certify AB, scenario D.

## 1. Container Apps

Vi deployar till Container Apps eftersom det bara finns ett API, så det behöver inte den frihet som man har med AKS i detta fall. Det är ett nytt bolag så det finns inte ett team som kan ha koll på AKS. Container Apps är redan tillräckligt bra när det kommer till att hantera mängder av trafik och skalning. När vi väljer Container Apps och inte AKS så får vi ett färdigt paket med resurser från Azure, så när det kommer till att till exempel köra https hela vägen går det inte, för Azure sköter den delen och skickar då http till API:t. Vi skulle då välja AKS när vi har ett team som kan hålla koll på AKS, och när vi har flera tjänster.

## 2. CI/CD

Först triggas pipelinen när man pushar till main, då den har trigger på main-branchen. Sedan byggs koden, och efter att koden byggts körs testerna. Sedan byggs en image som pushas till ACR och till sist deployas imagen till Container Apps. Detta är flödet för en ren, felfri pipeline. Men om det någonstans blir fel i processen så körs inte nästa delar förrän man fixat det och kör om pipelinen. Om man redan har en app live så påverkas den inte, då den kör på en fungerande image, och så fort den nya blir fungerande så blir den live istället. Testerna kontrollerar att `/health` svarar och att ett påhittat certifikat aldrig godkänns av `/verify`.

## 3. IaC

Bicep används för att bygga det man behöver en gång och sedan kunna återanvända det. Om man till exempel behöver dev, prod och test så kan man bygga alla på samma sätt med en fil istället för att behöva klicka sig igenom samma resurser tre olika gånger. Det blir också mindre chans att bli fel än när man själv klickar sig igenom allt. Idempotens betyder att om man kör samma sak flera gånger så får man samma resultat, och det händer inget nytt om man inte ändrar på något. När man kör mallen flera gånger behöver man därför inte tänka på vad som redan finns. Rollerna får alltid samma guid så det är alltid samma id, och guid gäller bara för roller då resurser har fasta namn. Azure jämför den nya mallen med det som redan finns och ändrar bara det som är nytt.

## 4. Säkerhet

Vi eliminerar riskerna för läckta autentiseringsuppgifter genom att använda Azure Managed Identity istället för hårdkodade lösenord eller anslutningssträngar. Vår Container App har en System-Assigned identitet som via Azure RBAC tilldelats rollen *Storage Blob Data Contributor* direkt på vårt Storage Account, och koden använder `DefaultAzureCredential` för att begära kortlivade tokens. Känsliga värden, såsom `ADMIN_API_KEY`, skickas in som en säker Bicep-parameter (`@secure()`) som läses från en miljövariabel vid deploy, så värdet aldrig står i git eller syns i deployment-historiken. Om en hemlig nyckel av misstag skulle hamna i git-historiken betraktas den omedelbart som komprometterad och måste roteras i Azure samt raderas ur git-historiken via till exempel `git filter-repo`.

## Ekonomi

Priser från Azures priskalkylator, region Sweden Central, i SEK.

| Resurs | Beräkning | Kr/mån |
|---|---|---|
| Container Apps | 2 repliker × 0,25 vCPU × 0,5 GiB dygnet runt, minus gratiskvot | ≈ 319 |
| Container Registry | Basic, fast pris | 47,58 |
| Blob Storage | 1 GB, 8 000 skrivningar och läsningar | ≈ 1 |
| Log Analytics | under 5 GB gratis per månad | 0 |
| **Totalt** | | **≈ 367** |

Vid lansering med 40 kunder kostar lösningen ungefär 367 kr i månaden. Det mesta är Container Apps på 319 kr, sedan ACR på 47,58 kr, medan lagringen bara kostar runt 1 kr. Om kundbasen tredubblas kostar det ungefär 370 kr, nästan samma, eftersom kostnaden styrs av att 2 repliker alltid är igång och inte av antalet kunder. Dyrast är Container Apps, runt 87 procent av totalen, eftersom 2 kopior av appen alltid är igång. Vid fyrdubblad trafik blir `GET /certificates` en flaskhals, eftersom den hämtar varje certifikat en och en. Dessutom kan appen inte skala över 5 repliker. Om en länk delas 10 000 gånger på en dag händer nästan ingenting, eftersom de första 2 miljoner anropen varje månad är gratis. Det kostar runt 0,04 kr i blob-läsningar. Skulle det bli miljoner kan vi cacha verifieringssvaret, eftersom ett certifikat aldrig ändras, till exempel med headern `Cache-Control: public, max-age=86400` eller med Azure Front Door framför `/verify/{uuid}`. Vi har räknat med aktivt pris, men replikerna är vilande så länge ingen använder dem, så i verkligheten blir den delen billigare än beräknat.

## Designval

| Beslut | Alternativ vi valde bort | Varför |
|---|---|---|
| Privata blobbar, verifiering via `/verify/{uuid}` | Publika blob-URL:er | Via API:t kan vi logga, returnera ett kontrollerat svar och spärra återkallade certifikat |
| Managed Identity mot Storage och ACR | Connection string, ACR admin user | Ingen hemlighet finns som kan läcka eller behöver roteras |
| Pipelinen deployar bara appen, Bicep körs separat | Bicep i pipelinen | Service connection klarar sig med Contributor, infrastruktur ändras sällan |
| `az acr build` | `Docker@2` med egen registry-connection | Återanvänder samma federerade connection, ingen Docker behövs på agenten |
| `minReplicas: 2`, autoskalning upp till 5 | Skala till noll | Ingen kallstart och en replik kvar om en kraschar, taket skyddar budgeten |
| Standard LRS | ZRS eller GRS | Certifikat är små och billiga att återskapa, LRS räcker för lanseringen |
| Parameterfiler för dev och prod | En mall med fasta värden | Samma mall, olika replikantal per miljö |