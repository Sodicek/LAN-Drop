# Zotavení po přerušeném běhu

Příchozí soubory se nejprve zapisují do vlastního pracovního adresáře aplikace ve zvolené cílové složce. Teprve po kontrole délky, otisku SHA-256 a uložených dat se soubor přesune mezi dokončené. Existující dokončený soubor se nepřepisuje. Změna cílové složky platí pro nové přenosy; probíhající přenos dokončí zápis do původní složky.

Při spuštění sdílení a výběru cílové složky se aplikace pokusí odstranit vlastní neaktivní pracovní data z předchozích přerušených běhů. Vyžaduje přesnou evidenci vlastnictví, známé názvy dočasných souborů a ověření, že je nepoužívá jiný běh. Neprochází do vnořených složek, nenásleduje odkazy a nemaže libovolné soubory `.part`. Neznámá, poškozená nebo stále používaná data nechá zachovaná.

V okně se zobrazí počet odstraněných nedokončených přenosů a případné upozornění na zachovaná pracovní data. Jiná běžící instance může svá data oprávněně používat. [Diagnostický souhrn](DIAGNOSTICS.md) obsahuje pouze počty, bez názvů, cest a obsahu souborů.

## Postup po pádu

1. Spusť aplikaci znovu, vyber cílovou složku a spusť sdílení. Heslo certifikátu zadej znovu.
2. Před opakováním zkontroluj, zda už cílová složka obsahuje dokončený soubor. Pád nebo ztracená odpověď těsně po uložení může znamenat, že soubor je hotový, přestože telefon potvrzení nedostal.
3. Připoj a schval telefon znovu. Pro nedokončené soubory vytvoř novou žádost o přenos. Předchozí přístupy a schválení po restartu serveru neplatí.
4. Pokud pracovní data zůstala zachovaná, zastav ostatní instance LAN Drop používající stejnou složku a vyber ji znovu. Neznámé položky aplikace ponechá; prohlédni je ručně a nemaž soubory plošně podle přípony.

Úklid má omezený rozsah a počet kontrol. Platí pro složku vybranou při startu a další ručně vybrané cílové složky. Neprohledává všechny dříve použité složky na počítači. Starší cíl se zkontroluje při jeho opětovném výběru.

Dočasné soubory z verzí před rc.4 nemají tuto evidenci vlastnictví, a proto zůstanou zachované. Dokončené přijaté soubory se tímto úklidem nemažou. Fronta, přehled přenosů a schválení jsou stále omezené na aktuální běh; rozpracovaný přenos je potřeba poslat znovu.

Kontroly odmítají odkazy a ověřují cesty, ale nezaručují ochranu proti jinému místnímu programu měnícímu cesty mezi jednotlivými operacemi. Test pádu procesu nepotvrzuje odolnost proti výpadku napájení ani nenahrazuje zálohu. Testování na skutečných telefonech, po síti a na čistých Windows stále čeká na ověření.
