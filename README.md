# Data Exchange Hub

ASP.NET Core Razor Pages projektas vidiniam HUB puslapiui ir pirmai aplikacijai `Data exchange`.

## Versija v7.0

v7.0 pakeitimai orientuoti i SQL modeliu selectu atskyrima pagal vartotojo salies teises.

- `APIS`, `ARETE`, `HYDRA` ir `KAIROS` SQL selectai suskirstyti pagal salies katalogus, pvz. `Sql/Apis/LT/...`, `Sql/Arete/LT/...`.
- Vartotojas modelyje mato tik tu saliu selectus, kurioms turi aktyvia `BS_APP_ACCESS` teise pagal `APP_NAME`, `DOMEN_USER`, `COUNTRY` ir `ACCESS`.
- `ACCESS = USER` ir `ACCESS = ADMIN` suteikia salies selectu matomuma; `COUNTRY = BS` ispleciama i `LT`, `LV` ir `EE`.
- Jei vartotojas turi daugiau nei viena sali, tame paciame modelyje mato visu priskirtu saliu selectus.
- Esami LT selectai perkelti i `LT` katalogus, kad LV ir EE selectus butu galima prideti atskirai nekeiciant serverio prisijungimu logikos.
- `Send Mail File` modelio teisiu ir katalogu logika nekeista.

## Versija v6.0

v6.0 pakeitimai orientuoti i dideliu SQL rezultatu valdyma ir vienoda eksportavimo patirti visuose SQL modeliuose.

- `ARETE`, `APIS`, `HYDRA`, `ORACUL`, `ClientInfo`, `Themis` ir `DataExchange` turi `Export Excel` ir `Export CSV` veiksmus.
- CSV eksportas generuojamas su `sep=;` ir UTF-8 BOM, kad Excel atidarant faila duomenys iskart issiskaidytu i celes pagal `;` skirtuka.
- Eksporto metu rodomas uzimtumo pranesimas su formatu (`Excel` arba `CSV`), analogiskai `EXECUTE` vykdymo pranesimui.
- Po eksporto puslapyje parodomi KPI: eksportuotu eiluciu kiekis, eksportuotu stulpeliu kiekis ir formatas.
- Po `EXECUTE` dideli rezultatai vizualizacijoje puslapiuojami po 10 000 eiluciu. SQL rezultatas saugomas cache, todel spaudziant `Ankstesnis` / `Kitas` SQL uzklausa nevykdoma is naujo.
- Exportai naudoja pilna cache turima rezultata, todel eksportuojamos visos uzkrautos eilutes, ne tik aktyviai matomas 10 000 eiluciu puslapis.
## Paleidimas

```powershell
dotnet run
```

Pagal `Properties/launchSettings.json` aplikacija pasiekiama:

- `http://localhost:5056`
- `https://localhost:7205`

Development konfige realiu SQL slaptazodziu nelaikykite. Lokaliai susikurkite ignoruojama faila pagal sablona:

```powershell
Copy-Item appsettings.Local.example.json appsettings.Local.json
```

`appsettings.Local.json` irasykite realias `ConnectionStrings` reiksmes ir, jei reikia realios teisiu lenteles, nustatykite:

```json
"DataExchange": {
  "TestMode": false
}
```

Jei localhost matote tik `dbo.DataExchangeFiles` ir rezima `Testavimas be apribojimu`, vadinasi dar naudojamas `TestMode: true` arba nera `appsettings.Local.json` su `TestMode: false`.

## Deploy i Windows Server / IIS

Serverio testavimui naudokite IIS su Windows Authentication, kad aplikacija matytu realu `DOMAIN\user`, o ne lokalaus paleidimo ar AppPool vartotoja.

1. Serveryje idiekite:
   - .NET 9 Hosting Bundle.
   - IIS role su `Windows Authentication`.
   - Isjunkite `Anonymous Authentication`, ijunkite `Windows Authentication`.
2. Publikavimas is projekto katalogo:

```powershell
.\Deploy\Publish-Hub.ps1
```

3. Publikuoti failai bus:

```text
.\publish\DATA_EXCHANGE_HUB
```

4. Serveryje nukopijuokite `appsettings.Production.template.json` i:

```text
appsettings.Production.json
```

Ir jame irasykite realius SQL prisijungimus.

5. IIS site/application fizinis kelias turi rodyti i publikuota folderi. `web.config` jau paruostas:
   - `ASPNETCORE_ENVIRONMENT=Production`
   - `anonymousAuthentication=false`
   - `windowsAuthentication=true`

6. Excel importo perziuros saugomos `App_Data\DataExchange\ImportPreviews`, todel IIS app pool turi tureti `Modify` teise visam `App_Data\DataExchange` katalogui. Si zingsni reikia paleisti serveryje po failu nukopijavimo. Serveryje paleiskite PowerShell kaip Administrator ir ivykdykite:

```powershell
.\Deploy\Grant-AppDataAccess.ps1 -SitePath "C:\inetpub\DATA_EXCHANGE_HUB" -AppPoolName "DATA_EXCHANGE_HUB"
```

Jei serveryje neturite `Deploy` katalogo, paleiskite tiesiogiai:

```powershell
New-Item -ItemType Directory -Path "C:\inetpub\DATA_EXCHANGE_HUB\App_Data\DataExchange\ImportPreviews" -Force
icacls "C:\inetpub\DATA_EXCHANGE_HUB\App_Data\DataExchange" /grant "IIS AppPool\DATA_EXCHANGE_HUB:(OI)(CI)(M)" /T
```

Jei app pool naudoja ne standartini `IIS AppPool\<AppPoolName>` vartotoja, nurodykite tikra identity:

```powershell
.\Deploy\Grant-AppDataAccess.ps1 -SitePath "C:\inetpub\DATA_EXCHANGE_HUB" -Identity "DOMAIN\service.user"
```

Jei `DataExchange:StoragePath` production konfiguracijoje pakeistas i atskira absoliutu kataloga, ta kelia perduokite atskirai:

```powershell
.\Deploy\Grant-AppDataAccess.ps1 -SitePath "C:\inetpub\DATA_EXCHANGE_HUB" -AppPoolName "DATA_EXCHANGE_HUB" -StoragePath "D:\DATA_EXCHANGE_HUB\DataExchange"
```

Publikuojant galima perduoti app pool varda, kad skriptas tai padarytu automatiskai:

```powershell
.\Deploy\Publish-Hub.ps1 -OutputPath "C:\inetpub\DATA_EXCHANGE_HUB" -AppPoolName "DATA_EXCHANGE_HUB"
```

`Send Mail File` katalogams AppPool reikia `Read/Execute` teises i saliu katalogus. Serveryje paleiskite:

```powershell
.\Deploy\Grant-SendMailFileAccess.ps1 -AppPoolName "DATA_EXCHANGE_HUB"
```

Jei `Deploy` katalogo serveryje nera, paleiskite tiesiogiai:

```powershell
icacls "Q:\QLIKDATA\DATA\SEND_MAIL\LV" /grant "IIS AppPool\DATA_EXCHANGE_HUB:(OI)(CI)(RX)" /T
icacls "Q:\QLIKDATA\DATA\SEND_MAIL\LT" /grant "IIS AppPool\DATA_EXCHANGE_HUB:(OI)(CI)(RX)" /T
icacls "Q:\QLIKDATA\DATA\SEND_MAIL\EE" /grant "IIS AppPool\DATA_EXCHANGE_HUB:(OI)(CI)(RX)" /T
```

7. Patikrinimas:
   - Atidarius `Data exchange`, kaireje turi matytis realus Windows vartotojas.
   - Teises turi buti tikrinamos pagal `BS_EXCHANGE_ACCESS`.
   - Atidarius `ORACUL`, prieiga turi buti tikrinama per `APPLICATION_GetUserAccess`.
   - Atidarius `KAIROS`, prieiga turi buti tikrinama per `APPLICATION_GetUserAccess` su aplikacijos pavadinimu `KAIROS`.

Jei vietoje realaus vartotojo matomas AppPool arba serverio useris, IIS dar naudoja Anonymous arba aplikacija paleista ne per Windows Authentication.

## Teisiu rezimas

`appsettings.json` yra ijungtas SQL teisiu tikrinimas:

```json
"DataExchange": {
  "TestMode": false
}
```

Kai `TestMode` yra `false`, vartotojas mato tik tas lenteles, kurios jam priskirtos `[BLS_LOGIT_STAT].[dbo].[BS_EXCHANGE_ACCESS]` lenteleje.

Testavimui galima laikinai ijungti `TestMode: true`; tada vartotojas gauna pilnas teises testinei lentelei is `DataExchange:TestModeTableName`.

## SQL Server rezimas

1. SQL Server duomenu bazeje paleiskite `Database/DataExchangeHub.sql`.
2. `appsettings.json` irasykite `ConnectionStrings:DataExchangeSql`.
3. `DataExchange:TestMode` pakeiskite i `false`.
4. `[BLS_LOGIT_STAT].[dbo].[BS_EXCHANGE_ACCESS]` lenteleje pridekite Windows vartotojus ir lenteles:

```sql
INSERT INTO [BLS_LOGIT_STAT].[dbo].[BS_EXCHANGE_ACCESS]
    (TABLE_NAME, DOMEN_USER, [ACCESS], CAN_RECEIVE, CAN_UPLOAD, UPDATED_BY)
VALUES
    (N'[BLS_LOGIT_STAT].[dbo].[JUSU_LENTELE]', N'DOMAIN\user.name', N'ADMIN', 1, 1, SUSER_SNAME());
```

Teisiu kombinacijos:

- `CanReceive = 1`, `CanUpload = 1`: gali gauti ir ikelti.
- `CanReceive = 1`, `CanUpload = 0`: gali tik gauti.
- nera aktyvaus iraso lentelei: vartotojas tos lenteles Data Exchange sarase nemato.

## Data Exchange lenteles

Atidarius `Data exchange`, vartotojas mato tik tas lenteles, kurios jam priskirtos `[BLS_LOGIT_STAT].[dbo].[BS_EXCHANGE_ACCESS]`.

- Pagal nutylejima rodoma `Top 10` eilučių.
- Vartotojas gali pasirinkti `All`, kad matytu visa lentele.
- `CAN_RECEIVE = 1` leidzia perziureti lenteles duomenis.
- `CAN_UPLOAD = 1` leidzia prideti nauja irasa ir koreguoti esamus irasus, jei lentele turi `ID` arba pirminio rakto stulpeli.
- `Eksportuoti Excel` iskelia pasirinktos lenteles tuo metu rodoma informacija (`Top 10` arba `All`).
- Pries eksportuojant Excel galima pasirinkti, ar eksportuoti dabartini `Top` eiluciu kieki, ar visus aktyviais filtrais atrinktus irasus be `Top` ribojimo.
- `Template` iskelia `.xlsx` faila tik su pasirinktos lenteles headeriais.
- `Ikelti Excel` leidziamas tik su `CAN_UPLOAD = 1`; pries irasant tikrinama, ar Excel headeriai atitinka visus SQL lenteles stulpelius.
- `ID`, `UPDATE_TIME`, `UPDATE_BY` ir `UPDATED_BY` yra sisteminiai laukai: web forma ju nerodo, Excel importas juos ignoruoja, `UPDATE_TIME` pildomas dabartiniu laiku `yyyy.MM.dd HH:mm:ss`, o `UPDATE_BY` / `UPDATED_BY` - prisijungusiu Windows vartotoju.
- Vietoje `All` naudojamas saugesnis puslapiavimas: `Top 10`, `Top 100`, `Top 1000` ir `Ankstesnis` / `Kitas`.
- Vartotojas, turintis ir `CAN_RECEIVE = 1`, ir `CAN_UPLOAD = 1`, gali trinti paviene eilute arba aktyviais filtrais atrinkta bloka. Blokinis trynimas pirmiausia suskaiciuoja atrinktas eilutes ir tik po to leidzia patvirtinti trynima.
- Excel importas turi perziuros zingsni: failas validuojamas, parodomas eiluciu kiekis ir tik paspaudus `Patvirtinti importa` duomenys irasomi i SQL.
- Filtrai turi klientine validacija `IN` ir `BETWEEN` atvejams; pagal SQL stulpelio tipa web forma parenka data/skaitinius/bit laukus, kai tai galima padaryti saugiai.
- Puslapio apacioje rodoma naujausiu pasirinktos lenteles pakeitimu `Audit log` perziura. `UPDATE` ir `DELETE` audit irasus galima atstatyti, jei vartotojas turi ir gavimo, ir ikelimo teises.
- `BS_EXCHANGE_ACCESS.BEHAVIOR_PROFILE = 'CORRECTION_TYPE'` ijungia korekciju profili: nauju irasu pridejimas rankiniu budu nerodomas, paliekamas esamu reiksmiu koregavimas lenteleje. Excel importui privaloma nurodyti `CORRECTION_TYPE` (`DISCOUNT` arba `COMPENSATION`) ir `CORRECTION_YEARMONTH` formatu `YYYYMM`; sios reiksmes irasomos i atitinkamus SQL stulpelius. Excel importo metu `ID` headeris traktuojamas kaip `ID_TRP` ir rasomas i SQL `ID_TRP` stulpeli.

## Data Exchange audit log

Duomenu pakeitimai rasomi i `[BLS_LOGIT_STAT].[dbo].[BS_EXCHANGE_LOG]`.

- `INSERT` ir `IMPORT_INSERT` saugo ikelta irasa `ROW_DATA_AFTER` lauke.
- `UPDATE` saugo reiksmes pries ir po pakeitimo: `ROW_DATA_BEFORE` ir `ROW_DATA_AFTER`.
- `DELETE` saugo pasalinta irasa `ROW_DATA_BEFORE` lauke.
- `BATCH_ID` sugrupuoja viena vartotojo veiksma, pvz. Excel importo metu ikeltas eilutes.
- `ACTOR_USER`, `EVENT_AT`, `TABLE_NAME`, `KEY_COLUMN`, `KEY_VALUE` leidzia matyti kas, kada ir kur atliko pakeitima.

Pries naudojant ikelima, koregavima ar salinima naujoje aplinkoje paleiskite `Database/DataExchangeHub.sql`, kad butu sukurta log lentele.

## ORACUL

`ORACUL` HUB aplikacija perkelia `ORACUL.xlsm` logika i web aplinka.

- Prieiga tikrinama per `BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess`.
- Parametrai: savaites diena `1-7` ir KPI, kuris imamas is `BLSTXSTAT1.BLS_LOGIT_STAT.dbo.LSP_ORDERS_BY_WEEKDAY_POS` pagal `DISTINCT REPORTNAME`.
- `EXECUTE` kviecia `SNTXDATA.dbo.LSP_ORDERS_BY_WEEKDAY` ir rezultata rodo web lenteleje.
- `Export Excel` ir `Export CSV` naudoja pasirinktais parametrais jau cache turima rezultata, jei jis buvo uzkrautas per `EXECUTE`; jei cache nebera, uzklausa vykdoma is naujo ir sugeneruojamas failas.

## KAIROS

`KAIROS` yra atskira HUB aplikacija specialiu darbu poreikiams registruoti.

- Prieiga tikrinama per `BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess`; teisiu saltinyje aplikacijos pavadinimas turi buti `KAIROS`.
- Duomenys saugomi `[BLS_LOGIT_STAT].[dbo].[KAIROS_REQUESTS]`, audit log - `[BLS_LOGIT_STAT].[dbo].[KAIROS_REQUEST_LOG]`.
- Vartotojas pildo `TITLE`, `INF_PREK`, `QUANTITY_REQUEST`, `NEED_DATE`, `MIN_EXPIRATION_REQUEST`, `NETWORK_CLIENT` ir `COMMENT`.
- `CREATED_BY`, `CREATED_AT`, `UPDATE_TIME` ir `UPDATED_BY` pildomi automatiskai pagal prisijungusi Windows vartotoja ir serverio laika.
- `INF_PREK` turi buti tiksliai 7 simboliai, kiekis turi buti didesnis uz 0.
- Iki 11:00 `NEED_DATE` gali buti siandien; po 11:00 nauji poreikiai leidziami tik nuo rytojaus.
- `MIN_EXPIRATION_REQUEST` negali buti ankstesnis uz `NEED_DATE`.
- Irasai, kurie nebeatitinka 11:00 taisykles, uzrakinami trynimui ir atstatymui.


