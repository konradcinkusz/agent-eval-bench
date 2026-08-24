# Zacznij tutaj

Drzwi wejściowe do dokumentacji tego repozytorium.

Jest jej sporo i nie wszystko jest tym samym rodzajem dokumentu. Ta strona mówi,
którego rodzaju potrzebujesz, i odsyła we właściwe miejsce. Jeśli masz przeczytać
jedną stronę przed wszystkimi innymi — przeczytaj tę.

> **English version:** [`START-HERE.md`](START-HERE.md)

## Czym jest to repozytorium?

**Przyrządem do mierzenia, czy agent AI nadal zachowuje się tak, jak zostało to
wyspecyfikowane** — po zmianie kodu, po edycji promptu, po podmianie modelu albo
całkiem bez żadnej zmiany.

W środku jest agent kadrowy do wniosków urlopowych. **To badany okaz, nie
produkt.** Został wybrany, bo skupia wszystkie trudne właściwości naraz:
nieodwracalny zapis, arytmetykę dat między strefami czasowymi i świętami, reguły
uprawnień, powierzchnię ataku przez wrogie dane wejściowe, i oczywistą potrzebę,
by człowiek powiedział „tak". Repozytorium podsumowuje to jednym zdaniem:

> Agent to pretekst. **Poligon ewaluacyjny to właściwy produkt.**

## Cztery rodzaje dokumentu — i który jest Ci potrzebny

Ta dokumentacja podąża za [Diátaxis](https://diataxis.fr/) — obserwacją, że
dokumentacja obsługuje cztery różne potrzeby, a strona próbująca obsłużyć dwie
naraz nie obsługuje dobrze żadnej. Podział wynika z dwóch pytań: *pracujesz czy
się uczysz?* oraz *potrzebujesz działania czy wiedzy?*

| | **Praktyczne** — działanie | **Teoretyczne** — wiedza |
|---|---|---|
| **Nauka** (zdobywanie umiejętności) | 📘 **Samouczki** — lekcje prowadzące za rękę przez pierwsze wykonanie czegoś | 💡 **Wyjaśnienia** — tło, kontekst i powody, dla których jest tak, a nie inaczej |
| **Praca** (stosowanie umiejętności) | 🔧 **Przewodniki** — przepisy na zadanie, które już rozumiesz | 📇 **Materiał źródłowy** — suchy, wyczerpujący opis maszynerii |

Wiersz wybieraj po tym, co robisz w tej chwili, a nie po tym, ile już wiesz.

### 📘 Samouczki — „nigdy tego nie uruchamiałem"

Zorientowane na naukę. Idziesz krok po kroku, wszystko działa, a na końcu widzisz
rzecz na własne oczy. Żadnych decyzji do podjęcia, żadnej teorii.

1. [**Pierwsze uruchomienie**](tutorials/01-first-run.pl.md) — sklonuj, uruchom,
   wpisz zdanie i zobacz, jak agent wykonuje całą pracę, po czym się zatrzymuje.
   Około 15 minut, bez danych dostępowych, bez zakładania kont.
1. [**Twój pierwszy scenariusz**](tutorials/02-your-first-scenario.pl.md) —
   napisz scenariusz, który nie przechodzi, a potem spraw, żeby przeszedł. To
   jest pętla, dla której obsługi istnieje całe to repozytorium. Około 25 minut.

### 🔧 Przewodniki — „wiem, czego chcę; jak to zrobić?"

Zorientowane na zadanie. Każdy zakłada, że rozumiesz już otaczające pojęcia, i od
razu przechodzi do kroków.

- [Uruchamianie ewaluacji](how-to/run-the-evals.pl.md) — cały zestaw, jedna
  warstwa albo jeden scenariusz
- [Dodanie scenariusza](how-to/add-a-scenario.pl.md) — wraz z regułami, które
  wymusza walidator
- [Dodanie zachowania](how-to/add-a-behaviour.pl.md) — najpierw specyfikacja,
  potem scenariusz, dopiero potem krok potoku
- [Diagnozowanie nieprzechodzącego scenariusza](how-to/debug-a-failing-scenario.pl.md)
  — czytanie śladu, gdy asercja świeci na czerwono
- [Włączenie sędziego](how-to/enable-the-judge.pl.md) — zamiana Warstwy 2 ze
  `skipped:no-credential` w prawdziwy przebieg

### 📇 Materiał źródłowy — „jak dokładnie nazywa się ta rzecz?"

Zorientowany na informację. Do wyszukiwania, nie do czytania od deski do deski.

| Dokument | Co opisuje |
|---|---|
| [`SPEC.md` §2](SPEC.md) 🇬🇧 | Słownik: narzędzia, zdarzenia śladu, wyniki tury, identyfikatory |
| [`SPEC.md` §3–§4](SPEC.md) 🇬🇧 | 16 zachowań i 7 twardych warunków, po identyfikatorach |
| [`SPEC.md` §6](SPEC.md) 🇬🇧 | 7 odmów i to, jak każda z nich musi wyglądać |
| [`dokumentacja.pl.html` §5](dokumentacja.pl.html) | Kompletny słownik śladu — każde zdarzenie, atrybut i zamknięty zbiór wartości |
| [`dokumentacja.pl.html` §16–§18](dokumentacja.pl.html) | Klucze konfiguracji z domyślnymi wartościami, powierzchnia HTTP, wszystkie sufity wydatków |
| [`evals/schema/scenario.schema.json`](../evals/schema/scenario.schema.json) | Format pliku scenariusza, wymuszany |
| [`evals/rubrics/judge.yaml`](../evals/rubrics/judge.yaml) | Pięć rubryk, ich skale, progi i zakotwiczenia |
| [`flyio/SECRETS.md`](../flyio/SECRETS.md) 🇬🇧 | Każdy sekret i to, co degraduje się bez niego |

### 💡 Wyjaśnienia — „dlaczego jest to zbudowane właśnie tak?"

Zorientowane na zrozumienie. Czytaj, gdy chcesz uzasadnienia, a nie kroków.

| Dokument | Co wyjaśnia |
|---|---|
| [`DIAGRAMS.md`](DIAGRAMS.md) 🇬🇧 | Cały system jako 22 diagramy — architektura, przepływy, pętla ewaluacji, dostarczanie |
| [`dokumentacja.pl.html`](dokumentacja.pl.html) | Pełna dokumentacja techniczna, 26 sekcji w 7 częściach |
| [`index.pl.html`](index.pl.html) / [`index.html`](index.html) 🇬🇧 | One-pagery — argument, dla czytelnika po raz pierwszy |
| [`FINDINGS.md`](FINDINGS.md) 🇬🇧 | Co zestaw naprawdę wyłapał: czternaście defektów, siedem w samym przyrządzie |
| [`CALIBRATION.md`](CALIBRATION.md) 🇬🇧 | Dlaczego sędzia musi zgadzać się z człowiekiem, zanim cokolwiek zablokuje |
| [`PRODUCTION.md`](PRODUCTION.md) 🇬🇧 | Co się zmienia, gdy to działa naprawdę, i co po cichu przestaje działać |
| [`DEVIATIONS.md`](DEVIATIONS.md) 🇬🇧 | Gdzie to repozytorium świadomie odchodzi od standardów, którymi jest mierzone |
| [`adr/`](adr/README.md) 🇬🇧 | Pięć decyzji architektonicznych, każda z odrzuconymi alternatywami |

## Jedna myśl, którą warto mieć przed wszystkim innym

**Eval to nie jest test w CI.** To pomiar zachowania systemu względem
specyfikacji, a bramka w CI jest tylko jednym z czterech miejsc, gdzie ten pomiar
konsumujesz:

| Kiedy | Po co | Analogia klasyczna |
|---|---|---|
| W pętli developerskiej | Iterujesz prompt i patrzysz, jak zmienia się odsetek zdanych — evale jako narzędzie *projektowania* | red-green-refactor |
| Przy zmianie | Regresja względem zapisanego wyniku bazowego | testy regresyjne |
| Przy wyborze modelu | Ten sam zestaw na różnych modelach, decyzja na liczbach | benchmark |
| Ciągle na produkcji | Prawdziwe sesje oceniane tym samym aparatem | monitoring / SLO |

A wyzwalaczem dla drugiego wiersza **nie** jest „zmiana w kodzie". W systemie
z modelem językowym zachowanie kształtują: kod, prompt systemowy, opisy narzędzi,
definicja agenta, wersja modelu i jego parametry oraz dane do wyszukiwania —
a połowa z tego nie jest kodem w ogóle. Dlatego `prompts/` i `agents/` są tutaj
ścieżkami wyzwalającymi evale i dlatego sprawdzenie w CI odrzuca pull requesta,
który je zmienia, nie ruszając specyfikacji.

Najkrótsza wersja:

> Testy odpowiadają na pytanie *„czy kod robi to, co napisałem?"*
> Evale — *„czy system robi to, co wyspecyfikowałem?"* — niezależnie od tego,
> która ruchoma część się zmieniła i czy w ogóle cokolwiek się zmieniło.

## Konwencje obowiązujące w całości

- **Nazwy są prawdziwe.** Nazwy klas, kroków, zdarzeń śladu i plików workflow
  w każdym dokumencie są skopiowane ze źródeł, więc da się je wyszukać. Gdy
  dokument i kod się nie zgadzają, rację ma kod, a dokument jest błędem
  (`REPO-BASELINE.md` §8).
- **Liczby mieszkają w jednym miejscu.** Sumy scenariuszy i asercji są
  przeliczane w [`FINDINGS.md`](FINDINGS.md) i nie są powtarzane gdzie indziej —
  liczba przepisana do prozy to liczba, która dezaktualizuje się przy następnym
  commicie.
- **Rzeczy niepochlebne są zapisane wprost.** [`DEVIATIONS.md`](DEVIATIONS.md)
  istnieje po to, żeby czytelnik nigdy nie musiał niczego wnioskować. Dwie warte
  poznania, zanim cokolwiek ocenisz: adapter MCP nigdy nie działał wobec żywego
  serwera, a pętla produkcyjna jest podłączona, ale nigdy nie przepłynął przez
  nią prawdziwy ruch.
- **Strony dwujęzyczne mają bliźniaka `.pl`.** Samouczki, przewodniki i ta strona
  istnieją po angielsku i po polsku; sprawdzenie w CI
  ([`scripts/check-doc-parity.mjs`](../scripts/check-doc-parity.mjs)) odrzuca
  zmianę jednej połowy bez drugiej. Cała reszta — specyfikacja, scenariusze,
  definicja agenta, kod źródłowy — jest wyłącznie po angielsku.
