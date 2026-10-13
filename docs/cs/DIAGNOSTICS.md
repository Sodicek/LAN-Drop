# Kontrola připojení a diagnostický souhrn

Před sdílením přes HTTPS vyber síť a certifikát, zadej jeho heslo a klikni na **Zkontrolovat certifikát**. Stejná kontrola proběhne před spuštěním sdílení. Ověří soukromý klíč, platnost, použití pro server a vybranou IP adresu. Důvěru telefonu ani dostupnost počítače po síti tím nepotvrzuje.

Souhrn ukáže adresy a konec platnosti certifikátu. Při zbývajících sedmi dnech nebo méně se zobrazí upozornění. Po změně IP nebo skončení platnosti vyber nový certifikát a znovu nastav důvěru jeho autority v telefonu podle [návodu pro HTTPS](HTTPS.md). Aplikace během sdílení pravidelně sleduje platnost použitého certifikátu a po vypršení sdílení zastaví; tato kontrola může mít prodlevu.

Po restartu lze obnovit údaje sousedního veřejného `.ca.cer`: zkontroluj uložený certifikát. Cestu a otisk SHA-256 nabídne aplikace pouze po místním ověření, že autorita vydala použitý serverový certifikát. Chybějící nebo nesouvisející soubor se nenabídne ke kopírování. To stále nepotvrzuje důvěru na telefonu. Do telefonu nikdy neposílej `.pfx` ani heslo.

## Uložení souhrnu pro hledání chyby

Klikni na **Uložit anonymní diagnostiku**, vyber místní soubor JSON a před přiložením k hlášení chyby si ho prohlédni. Export je ruční a nikam nic neposílá. Funguje při spuštěném i zastaveném sdílení.

Souhrn obsahuje verzi aplikace, Windows, architekturu a verzi .NET, stav sdílení, režim HTTPS/HTTP, kategorii síťového profilu, výsledek kontroly certifikátu, upozornění na končící platnost a počty zařízení, žádostí a posledních přenosů.

Neobsahuje adresy, názvy sítí, počítače, uživatele nebo zařízení, cesty ani názvy souborů, jejich obsah nebo velikosti, otisky certifikátů, hesla, párovací odkazy, přístupové tokeny, výpisy serveru ani texty výjimek. Nezkontrolovaný certifikát má stav **NotChecked**. Souhrn není testem dostupnosti telefonu ani historií přenosů.

Do veřejného hlášení nepřikládej výpis serveru, uložená nastavení nebo soukromé certifikáty. Podrobnosti jsou v [bezpečnostních zásadách](../../SECURITY.md) a [anglickém návodu](../DIAGNOSTICS.md).
