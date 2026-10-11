# Android — vývojová verze

LAN Drop funguje na Androidu přes prohlížeč, bez instalace APK. Cílovým prohlížečem je Chrome; praktické ověření na skutečných telefonech ještě zbývá.

1. Připoj Windows a Android ke stejné důvěryhodné síti. V LAN Drop na PC vyber privátní/doménovou síť a spusť sdílení.
2. Načti QR fotoaparátem nebo čtečkou QR a otevři odkaz v Chromu. Podle telefonu můžeš použít i Google Lens.
3. Ověř rozpoznaný typ telefonu nebo ručně zvol Android. Zadej název a potvrď připojení na PC.
4. Na telefonu vyber soubory. Na PC přijmi jednu žádost s názvy souborů a celkovou velikostí. Telefon potom vypočítá kontrolní součty a odešle soubory postupně. Nech prohlížeč otevřený a displej rozsvícený až do potvrzení uložení.
5. Z PC vyber tento telefon jako příjemce a připrav soubory. V telefonu klepni na Stáhnout; soubory hledej v sekci Stažené soubory v Chromu. Ukládání řídí prohlížeč, aplikace neslibuje automatické přidání do galerie.

Pokud čtečka QR otevře vlastní prohlížeč a výběr souborů nebo stahování nefunguje, otevři odkaz v Chromu. Ruční volba Androidu mění nápovědu a navržený název; neobchází kontrolu schopností prohlížeče ani oprávnění.

Zamknutí displeje, přepnutí aplikace nebo změna sítě mohou přenos přerušit. Nech záložku otevřenou a po obnovení spojení použij Opakovat nedokončené soubory; na PC potvrď novou žádost. Potvrzené dokončené soubory se znovu neposílají. Nedokončené soubory se odesílají od začátku, pokračování od přerušeného místa a zaručený běh na pozadí zatím nejsou implementované. Pokud se ztratilo potvrzení úspěchu, před opakováním zkontroluj seznam na PC. Zrušení výběru uvolní nedokončené soubory z fronty a nesmaže dokončené soubory.

Windows aplikace má v rc.2 předvolené HTTPS. Před načtením zabezpečeného QR nastav výslovnou důvěru telefonu podle [návodu HTTPS](HTTPS.md). Volitelný vývojový režim HTTP zůstává nešifrovaný. Kontrolní součet ověřuje uloženou kopii na PC, ne staženou kopii na Androidu. Automatické testy nenahrazují skutečný telefon. Při hlášení problému uveď model, verzi Androidu/Chromu a typ sítě; nezveřejňuj párovací odkazy ani osobní soubory.
