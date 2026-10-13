# Volitelná místní historie

Historie je **ve výchozím stavu vypnutá**. Pro ukládání záznamů spusť sdílení a v části historie zapni **Uchovávat místní historii dokončení (volitelné)**. Aplikace ponechá nejvýše 200 posledních dokončených záznamů ve svých místních datech. Tuto volbu si pamatuje i pro další běhy.

Záznam obsahuje samostatný identifikátor, skutečný název dokončeného souboru, velikost, směr a čas. Neobsahuje cesty, jména zařízení, párovací nebo přístupové údaje, certifikáty ani obsah souborů. Samotný název může obsahovat osobní informace. Historie je běžný místní soubor JSON a je oddělená od [anonymní diagnostiky](DIAGNOSTICS.md), která názvy z historie neobsahuje.

Příchozí záznam znamená, že počítač ověřil délku, otisk a uložená data a dokončil zápis. Odchozí záznam potvrzuje přípravu souboru pro schválený telefon; nepotvrzuje stažení ani uložení v telefonu. Historie neposkytuje odkaz nebo oprávnění ke stažení a neověřuje pozdější existenci nebo obsah souboru.

## Restart a ovládání

- Uložený přehled lze číst i při zastaveném sdílení a po restartu aplikace.
- Zapnutí, vypnutí nebo vymazání historie vyžaduje spuštěný server sdílení a místní ovládání aplikace.
- Vypnutí zastaví nové záznamy, starší ponechá. Pro jejich odstranění použij **Vymazat záznamy místní historie**.
- Vymazání odstraní pouze záznamy, nikoli přijaté soubory, právě připravené soubory nebo oprávnění. Pokud zůstane ukládání zapnuté, další dokončené přenosy se opět zaznamenají.
- Restart obnoví pouze přehled. Předchozí přístupy telefonů, schválení a fronty se neobnovují.

Čekající, zrušené a neúspěšné přenosy se mezi dokončené nezapisují. Zápis historie je nezávislý na samotném uložení souboru. Pád nebo chyba úložiště mezi dokončením souboru a zápisem záznamu může znamenat, že soubor existuje bez řádku v historii. Před opakováním přenosu po ztracené odpovědi proto zkontroluj cílovou složku.

Úložiště je omezené a chráněné proti současnému zápisu dvou serverů se stejnými daty. Poškozený, přesměrovaný nebo nedostupný soubor se bez upozornění nepřepisuje a chyba historie neblokuje běžné přenosy. Nejde o ochranu před jiným místním programem měnícím cesty ani záruku odolnosti proti výpadku napájení či bezpečného vymazání disku. Fronta je stále omezená na aktuální běh a soubor se po přerušení posílá znovu.
