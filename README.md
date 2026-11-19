# Data Exchange Hub

Vidinis ASP.NET Core Razor Pages (.NET 9) aplikaciju HUB su Windows vartotoju atpazinimu ir atskiromis moduliu prieigos teisemis. Apima SQL uzklausu vykdyma, Excel / CSV eksportus, DIKE klientu susitarimus ir IRIS ataskaitu bei siuntimo parametru konfiguravima.

## Versija v7.3 (2026-10-28)

- 2026-11-19: QVD ataskaitos prieinamos tik atitinkamo modulio aktyvia ADMIN teise turintiems vartotojams. APIS / ARETE / HYDRA sarasuose USER nemato QVD vardu ir kiekiu; tiesioginis pasirinkimas, vykdymas, puslapiavimas ir eksportai blokuojami su HTTP 403. Bendras vykdiklis tikrina ADMIN ir KAIROS atveju. Esamos saliu teises islaikytos; teises tikrinamos pries cache naudojima. Praejo 158 QVD patikros, iskaitant 56 prieigos patikras.

- 2026-11-19: `EE_MANH_ADJUSTIMENT` atkurtas gimtas SQL `date` parametras ir tekstines ISO datos ribos abiejose UNION dalyse. `created_at` su devyniais sekundziu trupmenos skaitmenimis nebelyginamas su implicitiniu `datetime`, kuri sukeldavo tekstiniam parametrui pritaikytas DATEADD. Su `Tests/ApisParameters -- --sql` praejo 38 patikros, iskaitant dienos ribas su sintetiniais saltiniais ir tikra ExecuteAsync LT / EN: 2026-10-23 - 201 irasas, 2026-11-19 - 7 irasai. SQL lenteles nekeistos.

- 2026-11-18: QVD ataskaitu vykdymas prijungtas prie APIS, ARETE, HYDRA ir KAIROS. Palaikomi dieniniu failu sablonai, SELECT / grupavimas / JOIN ir esami eksportai. Instrukcija bei pavyzdziai pateikti skyriuje „QVD ataskaitos“. Windows-1257 skaitiniame eksporte lokalizuotas minuso zenklas normalizuojamas i ASCII, nekeičiant tekstiniu stulpeliu.
- 2026-11-18: Pridetas `LT_FLOWS_QVD_JOIN` pagal QlikView raporto keturis dieninius QVD saltinius. Jungimo raktas `RECNO` + `DORD`, pagalbiniai raktai tikrinami per MATCH_ROWS. SQL duomenu saltiniai nejungiami. Vykdiklis i laikinas atminties lenteles kelia tik uzklausoje naudojamus stulpelius.
- 2026-11-18: FLOWS QVD pavyzdyje prideti pasirenkami `INF_PREK`, `PREF`, `PREFSTOCK`, `DOCK` filtrai (`=`, `LIKE`, `IN`). APIS islaiko vartotojo filtru eile formoje, vykdyme ir cache rakte; OR grupe negali apeiti baziniu WHERE salygu.
- 2026-11-18: QVD pagrindinio saltinio WHERE taikomas skaitymo metu, pries kaupiant vykdymo lentele. FLOWS pagalbiniai saltiniai atrenkami pagal jau filtruotus `RECNO` + `DORD` raktus (`QVD_MATCH`). Nuskaitymo apimties ribos atskirtos nuo po filtru atmintyje laikomu duomenu ribu.

- 2026-11-17: LT operaciju uzklausose pridedamas `QUANTITY_UM`. `LT_OPERATION_PAST_DAY` ir `LT_OPERATION_BY_DATE` suvienodinta `UNION ALL` stulpeliu tvarka, kad matavimo vienetas nebutu konvertuojamas i skaiciu. Abu variantai patikrinti su `KGR`; papildoma `LT_OPERATION_BY_PERIOD` integracine patikra nutruko del SQL vykdymo laiko ribos.

- 2026-11-12: APIS parametras be papildomu taisykliu pagal nutylejima leidzia `=` operatoriu. EE `EE_MANH_ADJUSTIMENT` dienos filtras palaiko tekstines ISO 8601 datas su devyniais sekundziu trupmenos skaitmenimis; datos parametras perduodamas kaip SQL `date`.

- 2026-10-29: DataExchange palaiko sudetinius eiluciu raktus (pvz., `LSP_ORDERS_BY_WEEKDAY_POS`: `REPORTNAME + POSID`). Redagavimas, trynimas, auditas ir atstatymas naudoja visus rakto stulpelius; rakto laukai neredaguojami. `bit` langelis po dvigubo paspaudimo turi `true / false` pasirinkima. Pakeitimus reikia patvirtinti mygtuku `Issaugoti pakeitimus`.

- Puslapio antrasteje ir pradiniame puslapyje rodoma `v7.3`; aplikacijos surinkimo versija yra `7.3.0`.

- Windows SSO prisijungimas sesijoje islaiko serverio patvirtinta vartotojo varda, kad griztant i HUB ar modulius IIS uzklausa be Windows tapatybes neprarastu teisiu patikros vartotojo. Nauja Windows tapatybe visada turi pirmenybe ir atnaujina jau patvirtintos sesijos varda. Negaliojanti sesija nesuteikia prieigos.

- Griztant i HUB, praradus vartotojo tapatybe net ir su likusiu sesijos pozymiu, nukreipiama i prisijungima. Kataloge rodomas tikrinamas Windows vartotojas. Technines moduliu teisiu patikros klaidos atskiriamos nuo tikro teisiu nebuvimo: leidziami moduliai lieka matomi, pateikiamas pakartotines patikros veiksmas; kai nieko nepavyksta patvirtinti del klaidos, grazinama 503. Tuscios paieskos ir nuliniai KPI be pasiekiamu moduliu nerodomi.

- Prieigos prasymo nuoroda inicijuoja Windows autorizacija ir grazina i ta pacia forma ties `#access-request`. Neatpazintas vartotojas pirmiausia patvirtina Windows paskyra; pasirinkta aplikacija, prieigos tipas ir komentaras islaikomi iki 10 min. sesijoje. Prasymas issaugomas tik atskirai ji pateikus po autorizacijos, naudojant serverio patvirtinta varda. Vien paskyros patvirtinimas nesuteikia moduliu teisiu.

- Sutvarkytos eksporto ir importo juostos: DataExchange / THEMIS pavadinimas ir Top pasirinkimas lieka virsuje, veiksmai gauna visa ploti zemiau, viena Excel krastiniu varnele valdo eksporta ir sablona. KAIROS islaiko savo atskira valdikliu struktura; ORACUL siauruose ekranuose pereina i viena stulpeli.
- IIS paketas: `publish\DATA_EXCHANGE_HUB-v7.3`. Keliant nauja paketa issaugokite serverio `appsettings.Production.json` ir jo SQL prisijungimus. Vien paketo sukurimas serverio neatnaujina. Po ikelimo patikrinkite rodoma `v7.3`, grizima i HUB is modulio bei prieigos prasymo autorizacijos eiga.

Automatines patikros be realiu prisijungimo slaptazodziu ir pranesimu siuntimo:

```powershell
dotnet run --project .\Tests\HubAccess\Checks.csproj
dotnet run --project .\Tests\CsvExport\Checks.csproj
dotnet run --project .\Tests\DataExchangeEditing\Checks.csproj
dotnet run --project .\Tests\ApisParameters\Checks.csproj
# APIS integracine patikra tik skaito EE duomenis per tikra ExecuteAsync:
dotnet run --project .\Tests\ApisParameters\Checks.csproj -- --sql
# Pasirinktinai: 41 patikra naudojant tik atskiras GUID SQL lenteles ir atskira audita:
dotnet run --project .\Tests\DataExchangeEditing\Checks.csproj -- --sql
```

Prisijungimo patikru yra 71, CSV patikru - 27. CSV GET / POST pasirinkimo narsykles patikra: `Tests/CsvExport/BrowserChecks.html`. Paleidziant DEV automatinius SESHAT siuntimus isjunkite su `--SeshatReminders:Enabled=false`. Diegima IIS uzbaikite realaus vartotojo su teisemis ir vartotojo be teisiu patikra; vien vietiniai testai IIS konfiguracijos nepatvirtina.

## Versija v7.2

- Puslapio antrasteje ir pradiniame puslapyje rodoma `v7.2`, kad po diegimo butu matoma atnaujinta versija.
- Visuose veikianciuose CSV eksportuose (`APIS`, `ARETE`, `HYDRA`, `ORACUL`, `ClientInfo`, `THEMIS`, `DataExchange`) yra pasirinkimas `Windows-1257` be BOM. Nepazymejus lieka UTF-8 su BOM. Abiem atvejais islaikomas `;` skirtukas ir `sep=;` eilute.
- Windows-1257 variante visos antrastes ir reiksmes apgaubiamos kabutemis, vidines kabutes dvigubinamos, tuscios reiksmes rasomos kaip `""`. Nepalaikomi simboliai sukelia paaiskinama klaida, kad duomenys nebutu tyliai pakeisti klaustukais.
- APIS Windows-1257 eksportas naudoja SQL skaitiniu stulpeliu metaduomenis ir rezultato pradine kultura: desimtainis skirtukas yra taskas, tikslumas islaikomas. Tekstiniai kodai, ju nuliai pradzioje ir kableliai nekeiciami. UTF-8 formatavimas islaiko ankstesni elgesi.
- Eksporto mygtukai islaiko dydi; SQL uzklausos / proceduros pasirinkimo laukui skiriamas likes eilutes plotis. Siauruose ekranuose valdikliai persikelia i kita eilute.
- IRIS saltinio `CsvWindows1257` nustatymas issaugomas ir ikeliamas redagavimui. Stulpelis pridedamas per esama SQL schemos atnaujinimo mechanizma, esamiems saltiniams numatyta `false`. Faktinis IRIS siuntimo generatorius dar neintegruotas; busimas skriptas turi pritaikyti si nustatyma.
- Production prisijungimui reikalinga patikima Windows tapatybe. IIS autentifikacija inicijuojama per Windows handleri; be jo pateikiamas 401 su paaiskinimu. Prisijungimas kitu vartotojo vardu ir nepriskirtu moduliu prieiga blokuojami.
- Production sablonas paveldi aktualu moduliu kataloga, kad nesusidubliuotu ir nedingtu DIKE / IRIS. `Deploy/Test-HubIis.ps1` tikrina IIS nustatymus nekeisdamas serverio ir nerodydamas slaptazodziu. Publikavimas sustabdomas, jei kompiliavimas ar publish nepavyksta.

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
- Salia Excel eksporto mygtuko galima pazymeti `Excel langeliu krastines`: antrastems ir visai eksportuojamai duomenu sriciai, iskaitant tuscius langelius, uzdedamos juodos krastines. Pasirinkimas taip pat veikia `KAIROS`, `SPHINX` ir APIS detaliu eksportuose. Nepazymejus lieka iprastas formatas; CSV pasirinkimas neveikia.
- CSV eksportas generuojamas su `sep=;` ir UTF-8 BOM, kad Excel atidarant faila duomenys iskart issiskaidytu i celes pagal `;` skirtuka.
- Eksporto metu rodomas uzimtumo pranesimas su formatu (`Excel` arba `CSV`), analogiskai `EXECUTE` vykdymo pranesimui.
- Po eksporto puslapyje parodomi KPI: eksportuotu eiluciu kiekis, eksportuotu stulpeliu kiekis ir formatas.
- Po `EXECUTE` dideli rezultatai vizualizacijoje puslapiuojami po 10 000 eiluciu. SQL rezultatas saugomas cache, todel spaudziant `Ankstesnis` / `Kitas` SQL uzklausa nevykdoma is naujo.
- Exportai naudoja pilna cache turima rezultata, todel eksportuojamos visos uzkrautos eilutes, ne tik aktyviai matomas 10 000 eiluciu puslapis.
## QVD ataskaitos (2026-11-18)

`APIS`, `ARETE`, `HYDRA` ir `KAIROS` kataloguose `QVD` yra atskiras duomenu saltinio tipas, lygiagretus `Stat3` / `VM0`. Failai ir toliau turi `.sql` pletini:

- `Sql/Apis/LT/QVD/Processes/LT_EMP_TTS_QVD_SUMMARY.sql` - veikiantis pavyzdys.
- `Sql/Arete/LT/QVD/<sritis>`, `Sql/Hydra/LT/QVD/<sritis>`, `Sql/Kairos/LT/QVD/<sritis>` - kitu moduliu keliai.

`QVD` katalogo vardas yra butent toks; SQL Server jungtis vardu `QVD` nereikalinga. Lieka iprasta forma, LT / EN kalendoriai, puslapiavimas ir Excel / CSV eksportas. APIS sarase uzklausa pazymeta `(QVD)`. QVD uzklausoms papildomai reikia aktyvios **atitinkamo modulio ADMIN** teises lenteleje `BS_APP_ACCESS`: APIS ADMIN savaime nesuteikia HYDRA ADMIN. USER sarasuose QVD uzklausos ir ju kiekiai nerodomi; tiesiogines GET / POST nuorodos, eksportas ir cache puslapiavimas neapeina ribojimo. ADMIN teise tikrinama kiekvienoje uzklausoje, o bendras QVD vykdiklis taip pat neleidzia vykdyti be ADMIN politikos. Esamos modulio ir salies teises lieka galioti; failu saltinio salys / moduliai papildomai ribojami konfiguracijoje. Nauju SQL lenteliu ar naujo teisiu lygio kurti nereikia. Sena atskiro QVD failo perziura nepasikeicia.

### Saltinio konfiguracija

ARGUS QVD perziura naudoja `Apis:QvdRoots` katalogus. Ataskaitai galima naudoti ta pati kataloga per `PreviewRootIndex`, nekartojant jo kelio. `0` yra projekto `Data/Apis/Qvd`, `1` - pirmas netuscias `Apis:QvdRoots` katalogas, `2` - antras ir t. t. Keiciant saraso tvarka, reikia patikrinti ataskaitu indeksus. Dabartinis `LT_EMP_TTS` pavyzdys naudoja `PreviewRootIndex: 1`:

```json
{
  "Apis": {
    "QvdRoots": ["\\\\FILE_SERVER\\SHARE\\LT\\EMP_TTS"]
  },
  "QvdReports": {
    "Sources": {
      "LT_EMP_TTS": {
        "PreviewRootIndex": 1,
        "Countries": ["LT"],
        "Modules": ["Apis", "Arete", "Hydra", "Kairos"]
      }
    }
  }
}
```

Jei ARGUS katalogas jau nurodytas, pakanka `QvdReports` dalies. ARGUS ir ataskaitos naudoja ta pacia katalogu parinkimo logika; pakeitus `Apis:QvdRoots` kelia, ji naudos abi vietos. Modulio ir salies leidimai tikrinami atskirai; ARGUS administratoriaus teises vartotojui del to nesuteikiamos. Trukstamas indeksas sukelia klaida. Saltiniui pasirenkamas tik vienas budas: `PreviewRootIndex` arba `RootPath`.

Nepriklausomam saltiniui galima nurodyti `RootPath`. Ribas taip pat galima koreguoti `QvdReports` skyriuje:

DEV `appsettings.Local.json`, o IIS `appsettings.Production.json`, prideti skyriu (taip pat yra `appsettings.Local.example.json`):

```json
{
  "QvdReports": {
    "Sources": {
      "LT_OPERATIONS": {
        "RootPath": "\\\\FILE_SERVER\\SHARE\\LT\\OPERATIONS",
        "Countries": ["LT"],
        "Modules": ["Apis", "Arete", "Hydra", "Kairos"]
      }
    },
    "MaxDays": 31,
    "MaxFiles": 256,
    "MaxScannedRows": 10000000,
    "MaxScanBytes": 2147483648,
    "MaxInputRows": 1000000,
    "MaxTotalBytes": 268435456,
    "MaxResultRows": 100000,
    "TimeoutSeconds": 60,
    "MemoryLimitMb": 256
  }
}
```

IIS proceso paskyrai reikia skaitymo teisiu i share ir NTFS katalogus. Serveryje naudokite UNC kelia: vartotojo `Q:` diskas IIS procesui gali buti nematomas. Vykdiklis QVD failu nekuria, nekeicia ir netrina. `DuckDB.NET.Data.Full` native bibliotekos turi patekti i publish paketa; naudokite Windows x64 IIS procesa.

### Kaip rasyti dieninio QVD SELECT

Sukurti, pvz., `Sql/Apis/LT/QVD/Processes/LT_QVD_OPERATIONS.sql`:

```sql
-- SQL_DESCRIPTION: Operaciju suvestine is dieniniu QVD failu
-- SQL_PARAM_MUST: @SNDATE|date||Pirma diena (imtinai)
-- SQL_PARAM_MUST: @ENDATE|date||Paskutine diena (imtinai)
-- SQL_PARAM_MUST: @OWNER|text||Savininko kodas
-- QVD_SOURCE: operations|LT_OPERATIONS|{date:yyyy.MM.dd}_OPERATIONS.qvd|@SNDATE|@ENDATE
-- QVD_COLUMN: operations|QUANTITY|decimal

SELECT _QVD_DATE AS NDATE, OWNER, ITEM, SUM(QUANTITY) AS QUANTITY
FROM operations
WHERE OWNER = @OWNER
GROUP BY _QVD_DATE, OWNER, ITEM
ORDER BY _QVD_DATE, OWNER, ITEM;
```

Pasirinkus nuo `2026-11-16` iki `2026-11-17`, atidaromi tik `2026.11.16_OPERATIONS.qvd` ir `2026.11.17_OPERATIONS.qvd` saltinio kataloge. `operations` yra lenteles alias, `LT_OPERATIONS` - konfiguracijos saltinio raktas; `FROM` rasomas alias, o ne Windows failo kelias.

Kelio sablonai: `Operations_{date:yyyyMMdd}.qvd`, `{date:yyyy}/{date:MM}/Operations_{date:yyyyMMdd}.qvd`. Vienos dienos uzklausai naudoti ta pati parametra abiejose ribose: `|@NDATE|@NDATE`. `*` ir `?` leidziami tik failo pavadinime, pvz. `Operations_{date:yyyyMMdd}_*.qvd`; katalogai neskaitomi rekursyviai. Statiniam failui datos parametru nereikia: `-- QVD_SOURCE: products|LT_OPERATIONS|Products.qvd`.

Kiekvienas pasirinkto laikotarpio failas turi egzistuoti. Trukstama diena, sugadintas arba formavimo metu pakites failas, skirtingos dienu schemos ar virsyta riba nutraukia visa uzklausa; nepilnas rezultatas tyliai nepateikiamas. Egzistuojantis failas su 0 irasu yra leistinas. `_QVD_DATE` yra diena is failo kelio sablono (statiniam failui NULL), `_QVD_FILE` - santykinis failo kelias. Laiko juostos neperkeliamos.

### Lauku tipai ir JOIN

Nenurodyti QVD laukai skaitomi kaip tekstas: issaugomi kodai ir nuliai pradzioje, iskaitant Qlik dual reiksmes. Skaiciavimams tipa nurodyti komentaru `-- QVD_COLUMN: alias|LAUKAS|tipas`. Palaikomi `text`, `int`, `number` (double), `decimal` (DECIMAL(38,10)), `date`, `datetime`, `bit`. `date` / `datetime` priima ir skaitines Qlik datas. Netinkamos reiksmes neatmetamos ir neverciamos i 0 - pateikiama lauko klaida. Daugiau nei 10 trupmenos skaitmenu turinti `decimal` reiksme atmetama; tokiam saltiniui rinkites `number` arba is anksto paruostus duomenis. `_QVD_DATE` ir `_QVD_FILE` jau turi tinkamus tipus.

Papildomas `QVD_SOURCE` leidzia JOIN tarp skirtingu QVD failu rinkiniu:

```sql
-- QVD_SOURCE: products|LT_OPERATIONS|Products.qvd
SELECT o.ITEM, p.NAME, SUM(o.QUANTITY) AS QUANTITY
FROM operations o
LEFT JOIN products p ON p.ITEM = o.ITEM
WHERE o.OWNER = @OWNER
GROUP BY o.ITEM, p.NAME
ORDER BY o.ITEM;
```

Palaikomi stulpeliai, `AS`, `WHERE`, parametrai `@VARDAS`, `ORDER BY`, `DISTINCT`, `GROUP BY`, `HAVING`, `SUM` / `COUNT` / `AVG` / `MIN` / `MAX`, `INNER` / `LEFT JOIN`, CTE, `UNION ALL` ir isorinis `TOP (sveikas_skaicius)`. `JOIN` daromas tarp deklaruotu QVD alias; tiesioginis SQL Server ir QVD JOIN vienoje uzklausoje nepalaikomas.

Leidziamas vienas SELECT. Nera SQL proceduru, INSERT / UPDATE / DELETE, SELECT INTO, DDL ar savavalisko isoriniu saltiniu skaitymo. Tai SELECT poaibis, ne pilnas T-SQL serveris: naudokite `CAST` / `TRY_CAST`, o ne SQL Server `CONVERT` su stiliaus kodu. Palaikomos paprastos tekstu / skaiciu funkcijos (`UPPER`, `LOWER`, `LEN`, `LTRIM`, `RTRIM`, `TRIM`, `REPLACE`, `CONCAT`, `SUBSTRING`, `LEFT`, `RIGHT`, `ABS`, `ROUND`, `FLOOR`, `CEILING`, `COALESCE`, `NULLIF`, `ISNULL`, `YEAR`, `MONTH`, `DAY`) ir esami parametru `IN` filtrai. Nezinomos funkcijos atmetamos. Duomenys laikomi uzklausai izoliuotoje atmintyje, isoriniai DuckDB saltiniai bei extension diegimas isjungti. Vienu metu vykdomos daugiausia 2 QVD uzklausos.

I vykdymo lenteles keliami tik SELECT, filtruose, JOIN, CTE, grupavime ar rikiavime pamineti QVD stulpeliai; stulpeliu vardai surenkami is SQL sintakses medzio. `SELECT *` / `alias.*` palieka visus laukus. `COUNT(*)` veikia ir neikeliant fiziniu stulpeliu reiksmiu.

Failai skaitomi po viena, o pagrindinio saltinio WHERE salygos taikomos pries kaupiant vykdymo lentele. Paprasti tekstiniai `=` filtrai tikrinami pries konvertuojant kitus laukus; LIKE, IN, skaiciu ir funkciju salygos tikrinamos SQL variklyje riboto dydzio porcijomis (iki 2048 eiluciu arba apie 1 MiB tekstinio turinio). Tik atitike irasai paliekami tolimesniems JOIN. Is WHERE iskeliamas tik saugus pagrindinio saltinio salygu poaibis: OR grupe neskaidoma, kitos JOIN puses salygos neperkeliamos, pasikartojantis saltinio naudojimas / self JOIN, RIGHT / FULL JOIN ir neaiskus atvejai paliekami galutiniam SELECT. Galutine WHERE salyga islaikoma visa.

QVD failai neturi siam vykdikliui prieinamo WHERE indekso: pasirinktu dienu irasus vis tiek reikia nuskaityti, o skaitytuvas atidaro vieno failo simboliu duomenis. Sumažinamas kaupiamu vykdymo duomenu kiekis, ne failu dydis diske. Failu, schemu ir pilnumo patikros galioja net ir tusciam filtravimo rezultatui.

Ribos atskirtos: `MaxFiles` (256), `MaxScanBytes` (2 GiB) ir `MaxScannedRows` (10 mln.) apima visa pasirinktiniu saltiniu nuskaityma; `MaxInputRows` (1 mln.) ir `MaxTotalBytes` (256 MiB, ivertintas eiluciu turinys) apima visu saltiniu **po ankstyvu filtru** kaupiamus irasus. Viso .NET proceso atminties tai negarantuoja. Vieno failo riba lieka 128 MiB, rezultato - 100 tukst. eiluciu, DuckDB atminties - 256 MB. Virsijus riba dalinis rezultatas neatiduodamas.

### Pagalbiniu QVD saltiniu atranka pagal raktus

Jei pagalbine lentele reikalinga tik filtruotos pagrindines lenteles raktams, po `QVD_SOURCE` deklaraciju galima nurodyti:

```sql
-- QVD_MATCH: epl_raw|RECNO,_QVD_DATE|flows|RECNO,DORD
```

Formatas: `pagalbinis_alias|jo_raktai|anksciau_deklaruotas_alias|jo_raktai`. Saltiniu tvarka turi buti pagrindinis, tada priklausomi; raktu kiekis vienodas (iki 8). Iki JOIN / CTE grupavimo i `epl_raw` patenka tik irasai, kuriu `RECNO` ir failo diena atitinka jau atrinkto `flows` iraso `RECNO` ir `DORD`. Tikrinama tiksli SQL EXISTS salyga: islaikomi visi sutampantys dublikatai ir NULL nesutampa. Taip neprarandama MATCH_ROWS dubliu tikrinimo logika.

Tai papildoma saltinio apribojimo taisykle, ne automatinis bet kokio JOIN perrasymas. Ja rasyti tik kai pagalbinio saltinio nesusije irasai negali tureti itakos rezultatui; pvz., ji netinka bendrai viso saltinio SUM, RIGHT / FULL JOIN ar nepriklausomam to saltinio naudojimui kitame CTE. Jei taisykles nera, pagalbinis saltinis lieka neapribotas. FLOWS pavyzdyje visoms trims pagalbinems lentelems si taisykle jau nurodyta.

### Pasirenkami QVD filtrai APIS formoje

`SQL_PARAM_MUST` deklaruoja privaloma ivesties lauka, o `SQL_PARAM` - lauka, kuri vartotojas gali pasirinkti skiltyje `Pasirinktiniai filtrai`, paspaudes `Prideti filtra`. Pasirenkamam laukui nurodomi leidziami operatoriai ir tikras SQL laukas su aliasu:

```sql
-- SQL_PARAM: @INF_PREK|text||QvdReport.ItemFilter|operators==,like,in;expr=s.INF_PREK
-- SQL_PARAM: @PREFSTOCK|text||QvdReport.PrefstockFilter|operators==,like,in;expr=s.PREFSTOCK
```

Tame paciame SELECT po baziniu WHERE salygu ir pries ORDER BY nurodoma vieta papildomiems filtrams:

```sql
WHERE s.OWNER = @OWNER
/* SQL_PARAM_FILTERS */
ORDER BY s.DORD;
```

`expr=s.INF_PREK` rasomas pagal tikra uzklausos aliasa; FLOWS pavyzdyje tai `f.INF_PREK`, o ankstesniame SKC pavyzdyje - `s.INF_PREK`. Galima filtruoti ir JOIN lenteles lauka, pvz., `expr=p.PRODNAMELT`, net jei jis neitrauktas i SELECT. Tokiai deklaracijai uzklausoje turi egzistuoti atitinkamas aliasas ir QVD laukas.

`=` tikrina tikslia reiksme, `LIKE` be `%` / `_` iesko fragmento, o pateikus siuos simbolius naudoja irasyta sablona. `IN` priima kelias kableliais atskirtas reiksmes. Tuscias arba nepasirinktas filtras WHERE nekeicia. Visi aktyvus papildomi filtrai apgaubiami atskirais skliaustais ir prijungiami per AND prie baziniu salygu, todel vidinis OR neapeina privalomu salygu. Vartotojo pasirinkta filtru eile islaikoma SQL generavime, pakartotinai rodant forma ir cache rakte: skirtinga AND / OR tvarka negali grazinti kito pasirinkimo rezultato. Reiksmes perduodamos kaip parametrai, ne kaip SQL tekstas. QVD LIKE atvejui naudojama jam tinkama CONCAT / REPLACE sintakse; SQL Server uzklausoms islaikyta esama LIKE sintakse.

### EMP_TTS pavyzdys ir patikros

DEV: APIS -> Procesai -> `LT_EMP_TTS_QVD_SUMMARY (QVD)`. Naudojamas tas pats ARGUS perziuros `EMP_TTS` katalogas (`PreviewRootIndex: 1`) ir `{date:yyyy.MM.dd}_EMP_TTS.qvd` failai. SQL pateikia dienos / padalinio ivykiu ir darbuotoju kiekiu suvestine. Nurodykite, pvz., `2026-11-17` abiejose datos ribose.

```powershell
# Atskiri mazi QVD failai, be SQL Server ir laisku:
dotnet run --project Tests/QvdReports/Checks.csproj
# Papildomai tik skaitymo EMP_TTS pavyzdzio patikra:
dotnet run --project Tests/QvdReports/Checks.csproj -- --real
```

### FLOWS JOIN pagal QlikView raporta

`APIS -> Procesai -> LT_FLOWS_QVD_JOIN (QVD)` yra pradinis pateikto QlikView skripto QVD dalies pavyzdys. Vykdomas tik SELECT is failu; pateikto raporto ODBC jungtys, SQL uzklausos, kainorastu / kuro / svenciu perskaiciavimai ir vadybininku duomenys neperkeliami. Pradinio skripto su prisijungimu informacija projekte nesaugome.

Pavyzdzio failas: `Sql/Apis/LT/QVD/Processes/LT_FLOWS_QVD_JOIN.sql`. Naudojamos keturios saltinio `LT_FLOWS` dalys:

| Alias | Failas saltinio kataloge | Jungimas |
| --- | --- | --- |
| `flows` | `FLOWS_DB/{date:yyyy.MM.dd}_LT_FLOWS_DB.qvd` | Pagrindiniai irasai, taikomi pateikto skripto LT_FLOWS filtrai |
| `epl_raw` | `EPL/{date:yyyy.MM.dd}_EPL.qvd` | `RECNO` + failo diena = `flows.RECNO` + `flows.DORD` |
| `ibl_raw` | `IBL/{date:yyyy.MM.dd}_EPL.qvd` | Tas pats raktas; IBL failo pavadinimas taip pat baigiasi `_EPL.qvd` |
| `amount_raw` | `AMOUNT/{date:yyyy.MM.dd}_AMOUNT.qvd` | Tas pats raktas |

`LT_FLOWS.RootPath` DEV konfige yra `Q:\DATA\LT\FLOWS\FLOWS_FANAS_DAYOFF`. Pateikto serverio skripto atitikmuo yra `Q:\QLIKDATA\DATA\LT\FLOWS\FLOWS_FANAS_DAYOFF`; IIS aplinkoje nurodyti proceso paskyrai pasiekiama kelia, pageidautina UNC. `appsettings.Local.example.json` papildytas atskiru `LT_FLOWS` saltiniu. EMP_TTS / ARGUS katalogo nustatymas nesikeicia.

Pirmai patikrai abiejose datose rinktis `2026-11-17`. Pagal vartotojo papildyta uzklausa `@OWNER` ir `@OPERATION` yra privalomi; operacijos reiksmes pavyzdys - `OUTGOING OPERATION`. Papildomai galima pasirinkti `@INF_PREK`, `@PREF`, `@PREFSTOCK` arba `@DOCK` ir operatoriu `=`, `LIKE` ar `IN`. FLOWS pagrindiniai irasai atrenkami skaitymo metu, o EPL / IBL / AMOUNT kelia tik jau atrinktiems `RECNO` + `DORD` reikalingus irasus. Is keturiu saltiniu vis tiek skenuojamos pasirinktos dienos; nuskaitymui ir kaupiamiems duomenims taikomos atskiros ribos, aprasytos auksciau.

Saugomi prekiu kodai su nuliais pradzioje. Kiekiai ir sumos yra skaiciai, `DORD` - data, iskaitant Qlik skaitines datas. `DOCK = QVU0322237` atvejui palikti `INTERNAL OPERATION` ir `FAKE` pakeitimai; `CALC` irasams palikta atskira filtro isimtis. Tuscias `INVOICEOWNER` laikomas nepatenkanciu i atmestu kodu sarasa.

CTE sugrupuoja pagalbines lenteles iki vienos eilutes jungimo raktui, kad JOIN nepadaugintu kiekiu. `EPL_MATCH_ROWS`, `IBL_MATCH_ROWS`, `AMOUNT_MATCH_ROWS`: `0` - atitikmens nera, `1` - vienas atitikmuo, `>1` - pasikartojantis raktas. Pastaruoju atveju metrikos paliekamos tuscios, kad nebutu pasirinkta atsitiktine ar susumuota klaidinga reiksme. Trukstamas atitikmuo taip pat netampa nuliniu kiekiu. Jeigu operacijos `DORD` nepatenka i pasirinkta failu dienu intervala, ankstesnis failas automatiskai neikeliamas ir rodomas trukstamas atitikmuo.

Tai detali keturiu QVD sujungimo lentele. Kitu raporto QVD (`PRODUCTH`, `OWNER`, `ORDERBASE`, `PRODH_DORD` ir kt.) bei istorinio `Y:` archyvo marsruto siame pavyzdyje nera. `PRODUCTH.qvd` siuo metu apie 304 MB ir virsija numatyta 128 MiB vieno failo riba; jo prijungimui reikia atskiro apimties sprendimo. Qlik asociatyvaus modelio / `WHERE EXISTS` negalima aklai pakeisti paprastu LEFT JOIN: [Qlik dokumentacija](https://help.qlik.com/en-US/qlikview/May2024/Subsystems/Client/Content/QV_QlikView/Scripting/InterRecordFunctions/Exists.htm).

Sintetiniai testai tikrina kartojama RECNO skirtingomis dienomis, DORD ir failo dienos skirtuma, kelis pasikartojancius pagalbiniu lenteliu raktus, trukstamus atitikmenis, filtrus ir kiekiu tipu islaikyma. Tikru failu patikra papildomai palygina rezultato eiluciu skaiciu ir paleciu suma su atskirai nuskaitytu FLOWS_DB failu:

```powershell
dotnet run --project Tests/QvdReports/Checks.csproj -- --flows
```

Patikrinta 2026-11-18: 76 QVD patikros praejo. Tikru `2026-11-17` failu rezultatas - 93 596 eilutes, 23 stulpeliai; pirminio failo filtruotas eiluciu skaicius ir paleciu suma sutapo. Visose trijose pagalbinese lentelese kiekvienam rezultato irasui rastas vienas atitikmuo, pasikartojanciu raktu nebuvo.

Po papildomu filtru prijungimo praejo 89 QVD ir 29 APIS parametru patikros. Jos apima pasirenkamu filtru AND / OR grupavima, vartotojo eiliskuma formoje ir cache rakte bei SQL Server parametru regresija. Su vartotojo papildyta privaloma salyga `OPERATION = OUTGOING OPERATION` ir `OWNER = NULL` visu savininku bandymui, tos pacios dienos tikru failu rezultatas yra 76 809 eilutes ir 23 stulpeliai; paleciu suma bei filtruotas irasu skaicius vel sutapo su atskirai nuskaitytu FLOWS_DB saltiniu.

Po atrankos perkelimo i skaitymo etapa praejo 106 QVD patikros (iskaitant EMP_TTS ir FLOWS tikrus dieninius failus) bei 29 APIS parametru patikros. Atskirai patikrintas vartotojo `2026-11-01` - `2026-11-18` pavyzdys: `OWNER=SNX`, `OPERATION=OUTGOING OPERATION`, `PREFSTOCK=VN1`, `INF_PREK=M011292`. Rezultatas **451 eilute / 23 stulpeliai**; kiekis ir paleciu suma sutapo su nepriklausomai nuskaitytais pagrindiniais QVD. Is 72 failu (430 619 372 baitu) nuskaityta 4 957 207 eiluciu, bet i vykdymo lenteles pateko tik 1804: po 451 i `flows`, `epl_raw`, `ibl_raw` ir `amount_raw`. Ivertintas kaupiamu eiluciu turinys - 589 343 baitai; tai ne visas proceso RAM. DEV uzklausa truko 44,4 s.

```powershell
# Tikru 18 dienu failu filtruoto pavyzdzio patikra (be SQL jungciu):
dotnet run --project Tests/QvdReports/Checks.csproj -- --filtered-flows
```

### QVD rezultatu atminties matavimas (2026-11-18)

`Tests/QvdMemory` yra atskiras matavimo irankis, nekeiciantis web konfiguracijos ir QVD failu. Is realaus `2026.11.17_LT_FLOWS_DB.qvd` (98 923 eilutes) visu lauku parenkami 15 didziausia vidutini tekstini ilgi turinciu lauku. Saugomi tik lauku vardai ir ilgiu histogramos, ne pradines reiksmes. Is tu ilgiu sugeneruojama ir realiai atmintyje sukuriama po 500 tukst. / 1 mln. rezultato eiluciu. Tai apimties modeliavimas pagal realiu lauku ilgius, ne tiek skirtingu realaus QVD irasu nuskaitymas.

Parinkti laukai: `OPERATION_KAY`, `ISKRVIET`, `PRODH_DORD_K`, `NW_JOIN_KAY`, `PREF_PCK_TMP`, `OPERATION`, `RECNO`, `ORG`, `SKC_RECNO`, `DOCK_DAL`, `PAL_DAL`, `MIX_PAL_DAL`, `SENT2WAREHOUSETIME`, `ORD_DAL`, `ORGBRIEF`. Bendra vidutine eilutes tekstu apimtis - 305,84 simbolio; visu 15 lauku didziausiu rastu ilgiu suma - 573 simboliai. Pastarasis scenarijus konservatyvus: kiekviename lauke vienu metu naudojamas jo maksimumas.

Rezultatas kuriamas tokia pacia struktura kaip vykdiklyje: kiekvienai eilutei atskiras `Dictionary<string, string?>`, reiksmes - atskiri tekstiniai objektai, visi irasai laikomi sarase. GB cia yra 1 000 000 000 baitu.

| Scenarijus | 500 000 eiluciu | 1 000 000 eiluciu |
| --- | ---: | ---: |
| Vien laikomas rezultatas, realiu ilgiu pasiskirstymas | 0,80 GB | 1,60 GB |
| Vien laikomas rezultatas, visu lauku maksimalus ilgis | 1,08 GB | 2,15 GB |
| Rezultato kurimo proceso privataus atminties kiekio pikas, realiu ilgiu pasiskirstymas | 0,90 GB | 1,78 GB |
| Rezultato kurimo proceso privataus atminties kiekio pikas, visu lauku maksimalus ilgis | 1,20 GB | 2,38 GB |

Fizinio working set pikai buvo atitinkamai 0,93 / 1,81 GB ir 1,23 / 2,40 GB. Laikomo rezultato eilutese atimta pradine valdoma atmintis; procesu pikuose itraukta nedidele matavimo programos baze. Tai .NET 9 Windows x64 konsoles proceso matavimas; IIS bazine atmintis, GC politika, QVD skaitytuvas, JOIN, HTML ir eksportas papildomi. Cache naudoja ta pati rezultato objekta, taciau skirtingi uzklausu / parametru / vartotoju rezultatai gali kauptis atskirai.

Atskirame paprastos 15 VARCHAR stulpeliu lenteles DuckDB + rezultato formavimo bandyme su dabartine `memory_limit=256MB` ir isjungtu diskiniu laikinu saugojimu 500 tukst. realiu ilgiu pasiskirstymo eiluciu pasieke 1,46 GB privataus proceso atminties pika. Vadinasi, DuckDB limitas neriboja viso .NET proceso. 1 mln. bandymas sustojo kraunant apie 614 tukst. ivesties eiluciu; maksimaliu ilgiu bandymai sustojo ties mazdaug 410 tukst. eiluciu. Sie bandymai neskaito tikru QVD dar karta, nedaro JOIN ir neeksportuoja Excel; native duomenims generuojamas ASCII tekstas pagal tuos pacius UTF-16 ilgius.

Matavimo metu galiojusios rezultato ribos islaikytos: iki 100 tukst. rezultato eiluciu, 32 mln. bendru rezultato teksto simboliu, 256 MB DuckDB limitas, daugiausia 2 vienu metu vykdomos QVD uzklausos. Veliau atskirtos 10 mln. nuskaitymo ir 1 mln. po filtru kaupiamu ivesties eiluciu ribos, aprasytos auksciau. Ilgiu maksimumu scenarijuje teksto riba butu pasiekta ties 55 847 eilute, net pakelus eiluciu riba. Matavimo irankis eiluciu / teksto ribu netaiko, bet sustoja, jei lieka maziau nei 512 MiB fizines ar commit atminties arba procesas virsija 2304 MiB privataus kiekio. Keturi vien rezultato bandymai pasieke visa prasoma eiluciu skaiciu.

Pakartojimas (naudoti atskirus procesus nuosekliai):

```powershell
dotnet run --project Tests/QvdMemory/Checks.csproj -- profile Q:/DATA/LT/FLOWS/FLOWS_FANAS_DAYOFF/FLOWS_DB/2026.11.17_LT_FLOWS_DB.qvd .buildcheck/qvd-memory/profile.json
dotnet run --project Tests/QvdMemory/Checks.csproj -- run .buildcheck/qvd-memory/profile.json result 500000 observed .buildcheck/qvd-memory/result-500000-observed.json
dotnet run --project Tests/QvdMemory/Checks.csproj -- run .buildcheck/qvd-memory/profile.json result 1000000 max .buildcheck/qvd-memory/result-1000000-max.json
dotnet run --project Tests/QvdMemory/Checks.csproj -- run .buildcheck/qvd-memory/profile.json pipeline 500000 observed .buildcheck/qvd-memory/pipeline-500000-observed.json
```

`observed` naudoja realu kiekvieno lauko ilgiu pasiskirstyma, `max` - kiekvieno lauko ilgio maksimuma. `result` matuoja vien galutine rezultato struktura; `pipeline` papildomai krauna DuckDB lentele ir skaito jos SELECT. JSON ataskaitose atskirai pateikiami managed, private ir working set matavimai; jos laikomos ignoruojamame `.buildcheck/qvd-memory` kataloge.

### Planuojamas diskinis rezultatu saugojimas ir valymas

Tai busimo diskinio rezimo reikalavimai; dabartinis QVD vykdiklis rezultatus dar laiko atmintyje. Dideliu uzklausu JOIN, WHERE ir stulpeliu atranka bus atliekami pries issaugant galutini rezultata diske. Puslapiavimas ir eksportas naudos ta pati paruosta rezultata, jo viso neikeldami i RAM. Vykdymo metu prireikus bus leidziami ir kontroliuojami laikini tarpiniai duomenys diske.

Laikini rezultatai turi buti susieti su konkrecia prisijungimo sesija ir atsitiktiniu rezultato identifikatoriumi. Vien vartotojo vardo nepakanka: atskiros to paties vartotojo sesijos neturi trinti viena kitos aktyviu rezultatu. Visi galutiniai rezultatai, tarpiniai duomenys ir laikini eksportai laikomi atskirame, viesai nepasiekiamame `App_Data/QueryResults` kataloge. Valymas apima tik si katalogu medi ir jam priklausancius irasus.

Numatytas gyvavimo ciklas:

- Atsijungus, panaikinus sesija ar pakeitus patvirtinta prisijungimo tapatybe, tos sesijos rezultatai atsaukiami ir paruosiami istrynimui.
- Pasibaigus sesijai ar rezultato galiojimui, jo failai ir nuorodos cache pasalinami. Rezultatu pradiniai, konfiguruojami terminai derinami su esamu cache: 20 min. be naudojimo, daugiausia 1 val. nuo sukurimo. Ilgesne HUB sesija (siuo metu 8 val. neaktyvumo) rezultato galiojimo nepratesia virs sio maksimumo.
- Uzdarius narsykle serveris ne visada gauna patikima pranesima. Tokius likucius surenka periodinis valymas, numatytai kas 5 min.; narsykles uzdarymo ivykis gali tik paspartinti, bet negali buti vienintelis valymo mechanizmas.
- Pakartotinai suformavus ta pati vaizda, senas nebereikalingas rezultatas istrinamas. Nesekmingai, atsauktai ar nutrukusiai uzklausai sukurti daliniai failai valomi ir klaidos atveju.
- Vienkartiniai eksporto failai istrinami uzbaigus arba nutraukus ju perdavima. Aktyviai skaitomas / rasomas rezultatas turi naudojimo zyme: istrynimas atidedamas iki skaitymo ar eksporto uzbaigimo; jau atsauktam rezultatui nauji skaitymai nebeleidziami.
- Paleidus aplikacija ir periodiskai tikrinami po IIS persikrovimo, proceso nutrukimo ar nepavykusio trynimo like pasene / nebepriklausantys aktyviai sesijai failai. Valymas turi atsizvelgti i dar veikiancio IIS proceso aktyvu skaityma, jei procesu paleidimas persidengia.
- Nepavykes trynimas registruojamas ir kartojamas kitu valymo ciklu. Ilgai nenaudojami rezultatai negali likti diske vien del prarasto cache iraso ar sesijos pabaigos pranesimo.

Pries kiekviena trynima turi buti patikrintas absoliutus kelias ir jo priklausymas valomam katalogui; bendru duomenu katalogu rekursyvus valymas neleidziamas. Realizavimo patikros tures apimti atsijungima, galiojimo pabaiga be narsykles pranesimo, uzklausos atsaukima, aktyvu eksporta, IIS persikrovima ir atskiru sesiju izoliavima.

## Paleidimas

LT ir EN vartotojo sasajos tekstai saugomi `Resources/SharedResource.lt.resx` ir `Resources/SharedResource.en.resx`. Puslapiai juos naudoja per `L[...]`, o dinaminiai naršykles pranesimai gauna tik reikiamus vertimus is `Pages/Shared/_ClientLocalization.cshtml`. SQL parametru ir uzklausu aprasymai taip pat lokalizuojami pagal komentaro teksta. Keiciant tekstus reikia atnaujinti abi kalbas, islaikyti vienodus formatavimo argumentus ir nekeisti formu lauku vardu bei siunciamu techniniu reiksmiu.

Visuose moduliuose datos ir laikotarpiai pasirenkami bendru kalendoriaus mygtuku. LT kalba datos rodomos `YYYY-MM-DD`, EN kalba `DD/MM/YYYY`; datos su laiku islaiko ir laika, o menesio laikotarpiai rodomi `YYYYMM`. I serveri datos perduodamos ISO formatu, islaikant lauku ribas ir validacija. `CORRECTION_YEARMONTH` importo laukas ir toliau perduodamas kaip `YYYYMM`.

Pradzios ir pabaigos datos susiejamos toje pacioje formoje: pabaigos kalendoriaus minimumas yra pradzia, pradzios maksimumas - pabaiga. Netinkamas intervalas, ivestas ranka arba gautas pakeitus viena is datu, iskart parodo paaiskinima pasirinkta kalba ir blokuoja formos pateikima. Vienodos datos leidziamos; isjungta neprivaloma pabaiga neberiboja pradzios. SQL parametru laukams naudojamas `data-date-range-field` (pvz. `@SNDATE` / `@ENDATE`, `@START_DATE` / `@END_DATE`).

APIS SQL faile galima nurodyti Excel skaitini stulpelio formata komentaru, pvz. `-- SQL_EXCEL_NUMBER: PLL|0.000`. Kiekvienas stulpelis aprasomas atskira eilute, naudojant rezultato stulpelio pavadinima ir Excel formato koda (`0`, `0.00`, `0.000`, `#,##0.00` ir pan.). Eksporte atpazintos skaitines reiksmes tampa tikrais Excel skaiciais; tuscios reiksmes lieka tuscios, o neskaitinis tekstas issaugomas. Formatas taikomas ir su langeliu krastinemis, ir be ju. CSV ir puslapio lentele nesikeicia.

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
   - Ijunkite `Windows Authentication`. Viesam pradiniam puslapiui iki autorizacijos taip pat ijunkite `Anonymous Authentication`; apsaugotas darbo aplinkas tikrina aplikacijos autentifikacijos ir moduliu teisiu politika.
   - Jei `Anonymous Authentication` isjungtas, IIS paprasys Windows autorizacijos dar pries atidarydamas puslapi.
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

5. IIS site/application fizinis kelias turi rodyti i publikuota folderi. `web.config` nustato `ASPNETCORE_ENVIRONMENT=Production` ir `inprocess` talpinima. Autentifikacijos parametrai turi buti nustatyti paciame IIS:
   - `windowsAuthentication=true`
   - `anonymousAuthentication=true`, jei pradinis puslapis turi buti viesas ir autorizacija prasoma paspaudus prieigos prasymo mygtuka. `false` autorizuoja visa svetaine dar iki aplikacijos.

`Production` sablonas moduliu katalogo neperraso: jis paveldimas is naujo `appsettings.json`. Jei esamame serverio `appsettings.Production.json` liko senas `HubApplications` masyvas, ji pasalinkite issaugodami kitus nustatymus ir prisijungimus; kitaip konfiguracijos masyvu sujungimas gali dubliuoti modulius ar paslepti DIKE / IRIS.

Po ikelimo IIS serveryje administratoriaus Windows PowerShell lange paleiskite tik skaitymui skirta patikra (nurodykite tikra IIS svetaines pavadinima):

```powershell
.\Deploy\Test-HubIis.ps1 -SiteName "DATA_EXCHANGE_HUB"
# Jei HUB yra IIS application, papildomai: -ApplicationPath "/HubApp"
```

Skriptas nekeicia IIS, nesijungia prie SQL ir nerodo slaptazodziu. Po jo patikrinkite realu vartotoja su priskirtu moduliu ir vartotoja be teisiu, taip pat tiesiogine modulio nuoroda. Be Windows tapatybes Production prisijungimas negalimas; IIS atveju aplikacija inicijuoja Windows autentifikacija, o be jos handlerio grazina 401 su paaiskinimu.

Prisijungimo regresijos testai be realiu slaptazodziu, SQL ir pranesimu siuntimo:

```powershell
dotnet run --project .\Tests\HubAccess\Checks.csproj
```

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

Jei vietoje realaus vartotojo matomas AppPool arba serverio useris, tikrinkite Windows Authentication ir tapatybes perdavima aplikacijai. Vien Anonymous ijungimas nereiskia, kad Windows autentifikacija neveikia; viesas pradinis puslapis palaiko abi autentifikacijos parinktis.

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

## HUB prieigos patikra

- Nesekmingas HUB prisijungimas panaikina ankstesne prisijungimo sesija. Bandymas naudoti kito vartotojo duomenis blokuoja modulius iki sekmingo prisijungimo savo vardu; ankstesne Windows autorizacija sio blokavimo neapeina.
- Production prisijungimui reikalingas serverio atpazintas Windows vartotojas. Jei Windows vartotojas neatpazintas, prisijungimas vien ivestu vartotojo vardu ir slaptazodziu neleidziamas. Serveryje turi veikti Windows Authentication.
- `HubModuleAccess` taiko ta pacia prieigos patikra HUB sarasui ir tiesioginiams moduliu adresams. SQL arba prieigos tikrinimo klaida prieigos nesuteikia. Modulio vidines administratoriaus, saliu ir dokumentu teises tikrinamos papildomai.
- IRIS ir SEND MAIL prieiga tikrinama per `BLS_LOGIT_STAT.dbo.APPLICATION_GetUserAccess` aplikacijoms `IRIS` ir `SEND_MAIL`. Galima nustatyti atitinkamo `Iris` / `SendMail` skyriaus `AppName`, `AccessConnectionStringName` ir `AccessProcedureName`. Kitu moduliu esami prieigos saltiniai islaikomi.
- Prieigos uzklausos vartotoja nustato serveris; formoje pateiktas vartotojo vardas negali pakeisti uzklausos autoriaus. Uzklausejo prieiga neatsiranda automatiskai pateikus prasyma.

## IRIS Excel priedo nustatymai

IRIS SQL saltinio formos mygtukas „Perziureti SQL rezultata“ vykdo dar neissaugotus formos duomenis ir rodo tikras rezultato eilutes po forma, nekeisdamas saltinio nustatymu. SQL tekstui ir failams Microsoft ScriptDom tikrina visa sintakses medi: leidziami SELECT, CTE, JOIN, agregatai ir vidines uzklausos; nuolatiniu lenteliu DML / DDL, EXEC, NEXT VALUE FOR, OPENQUERY / OPENROWSET bei netiesioginis vykdymas blokuojami. Laikinoms # / ## lentelems leidziami CREATE TABLE, SELECT INTO, INSERT, UPDATE, DELETE, TRUNCATE ir DROP; galimi IF / BEGIN blokai, jei ju viduje tik leidziami veiksmai. DML taikinys turi buti tiesiogiai nurodytas laikinos lenteles vardu, ne aliasu; OUTPUT INTO neleidziamas. INLINE_SQL taip pat tikrinamas issaugant saltini. Abu # / ## vardu tipai perziuroje perrasomi i unikalius privacius # sesijos vardus, iskaitant OBJECT_ID tikrinimus ir stulpeliu kvalifikatorius. ## sintakse nepateikia bendros lenteles kitoms perziuroms ar vartotojams. Duomenys galioja tik vieno vykdymo metu; isorinis IRIS siuntimo vykdytojas tures taikyti ta pacia patikra ir izoliavima. Komentaruose bei tekstinese reiksmeje esantys SQL komandu zodziai patikros neapeina ir nesukelia klaidingu draudimu.

Vartotojas atskirai patvirtino programuotoju sukurtu proceduru vykdyma. STORED_PROCEDURE kelias tikrina sistemos kataloge, kad procedura egzistuoja ir nera sistemine, bei vykdo ja pagal pilna DB / schemos / proceduros pavadinima. Sisteminems proceduroms ir sisteminems DB prieiga siame kelyje neleidziama; proceduros neprilyginamos SELECT ir gali tureti programuotoju numatytu salutiniu veiksmu.

Numatyta perziuros riba: 500 eiluciu vienai lentelei (`Iris:PreviewMaxRows`), iki 3 lenteliu, iki 100 stulpeliu, iki 2000 simboliu vienai reiksmei ir bendras teksto biudzetas. SQL tekstas ribojamas iki 1 MB; `Iris:PreviewTimeoutSeconds` numatytasis laikas yra 30 s. SQL failams leidziamas projekto `Sql` katalogas ir papildomi `Iris:PreviewSqlRoots` katalogai; nuorodos bei junction katalogai neleidziami.

Perziuros lentele rodoma atskirame viso plocio bloke po KPI / adresatu / SQL saltinio formomis, pries siuntimo grafika. Rezultatai puslapiuojami po 50 eiluciu is jau gauto perziuros rezultato; puslapiu keitimas SQL pakartotinai nevykdo. Plati lentele slenkama horizontaliai savo konteineryje. SQL redaktorius turi savas slinkties juostas ir nuo rezultato dydzio neissiplecia.

Auditas saugomas `Iris:PreviewLogTableName` (numatyta `BLS_LOGIT_STAT.dbo.IRIS_QUERY_PREVIEW_LOG`) per `Iris:ConnectionStringName`. Registruojami serverio nustatytas vartotojas, pradzia / pabaiga UTC, saltinio ir KPI ID, ivestas SQL (`SqlText`) ir realiai vykdytas izoliuotas SQL (`ExecutedSql`) arba procedura, failo kelias, prisijungimo profilis, DB, busena, parodytu eiluciu kiekis, trukme bei klaida. Pradinis irasas ir vykdomas tekstas privalo buti issaugoti pries SQL vykdyma; jei nepavyksta, saltinis nevykdomas. Atmesti bandymai irgi registruojami. Nutruke STARTED irasai lieka matomi, jei procesas sustabdomas ar dingsta SQL rysys. Lenteles schema sukuriama automatiskai, esami irasai neperrasomi.

Regresiniai testai: `dotnet run --project Tests/IrisPreview/Checks.csproj`. Papildomiems SQL integraciniams testams vykdykite `dotnet run --project Tests/IrisPreview/Checks.csproj -- --sql` is projekto katalogo. Integraciniai testai naudoja vietinius sukonfiguruotus prisijungimus, kuria atskirus atsitiktiniais vardais pavadintus testinius objektus ir juos pasalina; tikru laisku nesiuncia.

- SQL saltinio formoje prie „Prisegti Excel“ galima pasirinkti „Excel langeliu krastines“. Pasirinkimas saugomas kiekvienam saltiniui ir atkuriamas ji redaguojant.
- `Iris:SourceTableName` lenteleje (numatyta `BLS_LOGIT_STAT.dbo.IRIS_REPORT_SOURCES`) naudojamas `ExcelGridlines BIT NOT NULL DEFAULT (0)` stulpelis. Trukstamas stulpelis pridedamas automatiskai per IRIS schemos patikra; esamu saltiniu pasirinkimas lieka isjungtas.
- Busimas siuntimo skriptas, kai `AttachExcel=1`, turi perskaityti `ExcelGridlines`: `1` reiskia langeliu krastines antrastei ir duomenu sriciai, `0` – be krastiniu. Bendras C# generatorius tai palaiko per `IExcelWorkbookService.CreateWorkbook(..., gridlines: source.ExcelGridlines)`. Pats IRIS siuntimo / Excel generavimo vykdymas siame etape dar neprijungtas.

## DIKE susitarimai

- Susitarimai saugomi `Dike:ConfigTableName` lenteleje (`BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_CONFIG`), pakeitimai – `Dike:LogTableName` (`BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_LOG`), naudojant `Dike:AccessConnectionStringName` jungti. Trukstamos lenteles sukuriamos automatiskai, esamos neperrasomos.
- Vienas klientas gali tureti kelis susitarimus. Sarase paspaudus klienta atidaromas konkretus susitarimas koregavimui; „Naujas susitarimas“ pradeda nauja irasa tam paciam klientui.
- „Naujo sukurimas / koregavimas“ pasirinkus klienta automatiskai uzkraunamas jo naujausias aktyvus susitarimas (jei aktyviu nera — naujausias issaugotas). Jei susitarimu keli, galima pasirinkti konkretu. Tuscia forma esamam klientui atidaroma tik aiskiai paspaudus „Naujas susitarimas“. DIKE derinimu sritis siuo metu skirta TARA.
- Sukurimas ir koregavimas kartu su auditu atliekami vienoje SQL transakcijoje. Auditas saugo prisijungusi vartotoja (ta pati `IUserNameResolver`, kaip prieigos tikrinimas), UTC laika ir visas reiksmes pries / po. Versija apsaugo nuo pasenusios formos perrasymo.
- Istorijoje galima ieskoti vartotojo, kliento ar pakeistos reiksmes; rodoma iki 200 naujausiu atrinktu ivykiu. Susitarimu sarase – iki 500 naujausiu irasu.
- Datos, pirmas laikotarpis, INF_PREK, procentas ir siuntimo nustatymai tikrinami serveryje. Pirmas laikotarpis issaugomas `YYYYMM`, nors kalendoriaus pateikiama `YYYY-MM` reiksme taip pat priimama.
- Vadybininkas rodomas prie kliento informacijos ir klientu sarase is SQL `MANAGER_FULLNAME`: `WM0.SNTXDB.dbo.OWNER.MANAGERID` jungiamas su `WM0.SNTXECOM.dbo.MANAGER.ID`, skaitomas `MANAGER.FULLNAME`. Saraso ir pasirinkto kliento uzklausos naudoja ta pati saltini. Vadybininkas susitarimo formoje neredaguojamas; ankstesnes reiksmes susitarimu JSON neperrasomos is formos.
- „Esamuose susitarimuose“ yra susitarimo dokumentu blokas: PDF / EML ikelimas (iki 10 MB), pastaba, perziura lange ir originalo atsisiuntimas. Kiekvienas ikelimas sukuria atskira faila, net jei pavadinimas kartojasi.
- Dokumentai saugomi SQL `Dike:DocumentTableName` (`BLS_LOGIT_STAT.dbo.CLIENT_RECONCILIATION_DOCUMENTS`), su turiniu, susitarimo ID, autoriumi ir laiku. Ikelimas / pasalinimas ir istorija atliekami vienoje transakcijoje; pasalinimas loginis, audito duomenys ir ankstesnis turinys lieka saugomi.
- Patvirtinta prieigos taisykle: visi DIKE prieiga turintys vartotojai gali skaityti ir prideti aktyviu SA/BLT klientu susitarimu failus. Kiekvienas dokumento veiksmas tikrina susitarima, klienta ir failo priklausyma. Salinti gali tik DIKE `ADMIN`, patikrintas per ta pacia SQL prieigos procedura (`ACCESS` / `ACCESS_LEVEL`); teise tikrinama ir serveryje.
- EML perziurai naudojama jau idiegta `MsgReader` MIME biblioteka. HTML laisko turinys atskirtas `sandbox` iframe, taikomas CSP ir draudziamas isorinis turinys. PDF atidaromas narsykles perziuroje. Vien dokumento ikelimas nereiskia faktinio derinimo patvirtinimo.
- Siuntimo adresatai, menesio dienos 1–31 ir laikas `Europe/Vilnius` issaugomi kaip nustatymai; DIKE automatini vykdyma ir formos generavima dar reikia prijungti.
- Laikas ivedamas ranka `HH:mm` arba pasirenkamas laikrodzio mygtuku is valandu 00–23 ir minuciu 00–59. Neegzistuojantis laikas, pvz. `10:66`, iskart pazymimas pranesimu; tikrinimas taip pat atliekamas serveryje.
- Automatinio formavimo perziura perskaiciuojama pakeitus laikotarpi, dazni, dienas, laika ar galiojimo pradzia. Grafikas prasideda tame paciame pirmo derinimo menesyje, menesiniu arba 3 menesiu zingsniu. Rodomos artimiausia dar nepraejusi data ir ankstesne data pagal grafika su savaites diena bei 24h laiku; menesyje nesancios dienos praleidziamos. Ankstesne suplanuota data nera faktinio ivykdymo patvirtinimas; faktinio paskutinio derinimo SQL saltini dar reikia prijungti. Neaktyviems ir pagal poreiki derinamiems susitarimams automatine data nerodoma. Vasaros laiko metu neegzistuojantis laikas pastumiamas viena valanda pirmyn, apie tai rodant paaiskinima.

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


