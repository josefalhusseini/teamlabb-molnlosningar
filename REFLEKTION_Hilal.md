# Reflektion Hilal Özkan

### 1. Din roll i teamet

Jag ägde infrastrukturen och driftsättningen. Jag skrev bicep, som bygger ACR, Storage Account med en privat container, Log Analytics, Container Apps Environment och Container Appen. Där satte jag upp Managed Identity på appen och rolltilldelningarna AcrPull och Storage Blob Data Contributor. Jag skrev också dev.bicepparam och prod.bicepparam, så att samma mall kör 1 till 2 repliker i dev och 2 till 5 i prod.

Jag byggde pipelines.yml med stegen bygg, test, az acr build och az containerapp update. Jag valde az acr build i stället för en Docker task eftersom det fungerade med samma service connection och inte krävde Docker på agenten. Jag lade också till integrationstesterna, autoskalningen och rollback instruktionen i README.

### 2. Det svåraste momentet

Längst fastnade jag på att pipelinen fick unathorized när Container Appen skulle hämta imagen från ACR. Jag kollade att appen hade rollen AcrPull på registret, och det hade den. Så behörigheten fanns, men det fungerade ändå inte.

Till slut förstod jag att appen inte visste att den skulle logga in mot registret med sin identitet. Rollen säger bara vad identiteten får göra. Lösningen var ett registries block i Bicep med server: acr.properties.loginServer och identity: system. När det var fixat startade appen men gick inte att nå. Porten stod på 80, från hello world imagen jag testade med först, medan .NET appen lyssnar på 8080. Efter det ändrade jag en sak i taget och testade direkt.

### 3. Vad förstår du nu som du inte förstod innan?

Jag förstår varför rolltilldelningar i Bicep skrivs med guid(). Bicep beskriver hur miljön ska se ut, inte vilka steg som ska köras. När mallen körs letar Azure upp varje resurs på dess namn. Finns den redan och ser likadan ut händer inget, och det är det som gör mallen idempotent.

Ett Storage Account har ett fast namn, så det hittas varje gång. En rolltilldelning måste däremot ha ett GUID som namn. Om det skapas slumpmässigt får tilldelningen ett nytt namn vid varje körning. Azure tror då att det är en ny resurs, och deployen kraschar eftersom samma roll redan finns. Med guid(storage.id, containerApp.id, roleId) blir namnet samma varje gång, eftersom det räknas fram från samma värden. Därför kan jag köra mallen hur många gånger som helst.

### 4. Vad skulle du göra annorlunda?

Jag skulle lägga in teststeget i pipelinen första dagen, innan det fanns någon riktig kod. Nu kom testerna in nästan sist, så under större delen av sprinten deployade pipelinen allt som gick att kompilera. Om ett fel hade gjort /verify godkände ett påhittat certifikat hade det gått rakt ut i produktion, och för den här kunden är det precis det felet som inte får hända. Med teststeget från början hade varje push kontrollerats.

### 5. Arkitektur och ekonomi

Lösningen kostar som mest runt 367 kr i månaden med 40 kunder. Container Apps står för ungefär 319 kr av det, eftersom två kopior av appen alltid är igång. Då blir det ingen väntetid och tjänsten är uppe även om en kopia kraschar. ACR kostar 47 kr och lagringen runt 1 kr. Jag har räknat med fullt pris för replikerna, men när de inte tar emot anrop är priset lägre, så fakturan bör bli lägre än så. Med 3 960 kr i intäkt är infrastrukturen under 10 procent. Om ni får tre gånger fler kunder blir kostnaden nästan samma.

Det mest sårbara idag är säkerheten. Alla kunder delar samma API nyckel för att lista certifikat, så om en kund läcker nyckeln kan någon se alla kunders certifikat. Det skulle jag åtgärda innan lansering, med egen inloggning per kund via Entra ID. Tekniskt är den svaga punkten listningen av certifikat, som hämtar varje certifikat för sig och blir långsammare ju fler som finns. Om en länk går viralt klarar systemet det. 10 000 visningar kostar några ören. Vid miljontals anrop når appen sitt tak på fem kopior, men eftersom ett certifikat aldrig ändras kan svaret cachas med Azure Front Door, så de flesta anrop aldrig når appen.