# Samouczek 2 — Twój pierwszy scenariusz

W [Samouczku 1](01-first-run.pl.md) patrzyłeś ręcznie, jak agent się zatrzymuje.
W tej lekcji sprawisz, że będzie na to patrzeć maszyna — na stałe, przy każdej
przyszłej zmianie w repozytorium.

Napiszesz scenariusz, zobaczysz, jak **nie przechodzi**, a potem sprawisz, żeby
**przeszedł**. Ta pętla jest tym, dla czego obsługi istnieje całe to
repozytorium.

**Potrzebujesz:** działającej konfiguracji z Samouczka 1 i około 25 minut.
**Nie potrzebujesz:** żadnych danych dostępowych. Wszystko tutaj działa offline.

> 🇬🇧 [English version](02-your-first-scenario.md) · ⬅ [Zacznij tutaj](../START-HERE.pl.md)

## Czym jest scenariusz

Scenariusz to **plik danych, nie kod**. Mówi trzy rzeczy: w jakim świecie jest
agent, co do niego powiedziano i co musi być prawdą na końcu.

Ponieważ jest danymi, przeczyta go ktoś, kto nie pisze w C#, i przeniósłby się bez
zmian do implementacji tego samego agenta w Ruby albo TypeScripcie.

## Krok 1 — Zacznij od działającego przykładu

Skopiuj scenariusz jednodniowego urlopu:

```bash
cp evals/scenarios/happy/hap-003-single-day-vacation-friday.yaml \
   evals/scenarios/happy/hap-009-single-day-vacation-thursday.yaml
```

Otwórz nowy plik. Zamienisz „piątek" na „czwartek".

## Krok 2 — Uczyń go swoim

Zmień cztery rzeczy. Resztę zostaw w spokoju.

**Identyfikator**, który musi zgadzać się z nazwą pliku:

```yaml
id: hap-009-single-day-vacation-thursday
```

**Tytuł i powód istnienia.** Pole `why` nie jest ozdobą — to ta część, którą za
rok przeczyta recenzent, gdy scenariusz przestanie przechodzić, i będzie musiał
zdecydować, czy myli się scenariusz, czy agent:

```yaml
title: A single day of vacation, asked for on the Tuesday before

why: >-
  The same off-by-one risk as hap-003, one day earlier in the week. Asked on a
  Tuesday, "Thursday" is two days away, and start and end must be the same date.
  An agent that adds a day to `end_date` books two days of somebody's allowance
  without either of them noticing.
```

**Zdanie, które mówi użytkownik:**

```yaml
conversation:
  - role: user
    content: Book me Thursday off
  - role: confirmation
    decision: approve
    content: Yes, go ahead
```

**Oczekiwane daty.** Tu jest celowy błąd — wpisz datę piątkową, dokładnie tak,
jak zrobiłby to zmęczony człowiek:

```yaml
  - assert: tool_called_with
    tool: request_time_off
    match: subset
    args:
      leave_type_id: lt-201
      start_date: '2026-08-13'
      end_date: '2026-08-14'
```

## Krok 3 — Sprawdź, czy plik jest poprawnie zbudowany

```bash
npm run validate:scenarios
```

Sprawdza schemat i reguły korpusu — **nie** uruchamia agenta. Powinieneś zobaczyć,
że liczy Twój plik:

```text
validate-scenarios: 36 scenarios valid.
```

Jeśli pomyliłeś identyfikator albo nazwę pliku, powie to precyzyjnie. Jest surowy
celowo: scenariusz, którego nie da się wczytać, to test, który po cichu nie
istnieje.

## Krok 4 — Uruchom i zobacz, jak nie przechodzi

```bash
dotnet test
```

Twój scenariusz uruchamia **prawdziwego agenta** — ten sam potok, którego demo
używało w Samouczku 1 — wobec świata testowego, z zegarem przypiętym do tamtego
wtorku.

Nie przechodzi. Komunikat nazywa Twój scenariusz i asercję, która nie została
spełniona: `request_time_off` zostało wywołane z `end_date` równym `2026-08-13`,
a Ty zapewniłeś `2026-08-14`.

**Przeczytaj to uważnie, bo o to w tym ćwiczeniu chodzi.** Zapisałeś to, w co
wierzyłeś. Agent zrobił coś innego. Dokładnie jedno z was się myli — i od teraz
jest maszyna, która nie pozwoli tej rozbieżności przejść po cichu.

## Krok 5 — Zdecyduj, kto się myli

W tym wypadku Ty. Zegar jest przypięty do wtorku 11 sierpnia 2026, „czwartek" to
13-ty, a jeden dzień zaczyna się i kończy tą samą datą.

Popraw:

```yaml
      start_date: '2026-08-13'
      end_date: '2026-08-13'
```

## Krok 6 — Zielono

```bash
dotnet test
```

Twój scenariusz przechodzi. Od teraz uruchamia się przy **każdym pushu**, a każda
przyszła zmiana, która sprawi, że agent zarezerwuje dwa dni na jednodniowy
wniosek, wywali ten scenariusz, zanim zdąży się scalić.

Właśnie dodałeś do repozytorium trwałą, mechaniczną gwarancję. To jest cała
pętla.

## Krok 7 — Udowodnij, że scenariusz w ogóle potrafi coś złapać

Test, który nigdy nie oblał, niczego nie dowodzi. Zepsuj to, czego pilnuje,
i zobacz, jak świeci na czerwono.

Zmień asercję z powrotem na `end_date: '2026-08-14'`, uruchom `dotnet test`
i potwierdź, że nie przechodzi. Potem zmień z powrotem.

Ten nawyk ma tu formalną nazwę — **przebieg mutacyjny** — a repozytorium
uruchamia go wobec czterech celowo zepsutych agentów przy każdym pushu. Przy
pierwszym uruchomieniu znalazł prawdziwą dziurę: dwa scenariusze asertowały
`at_least: 1` zamiast `times: 1`, więc zepsuty agent wysyłający ten sam wniosek
**dwa razy** przeszedł oba. To jest [F-1](../FINDINGS.md).

## Krok 8 — Poznaj regułę, która czyni odmowy prawdziwymi

Twój scenariusz to ścieżka pozytywna. Odmowy mają dodatkową regułę i warto ją
zobaczyć od razu.

Jeśli napiszesz scenariusz klasy `denied` albo `adversarial`, który zapewnia, że
odmowa nastąpiła, ale nie zapewnia, że zakazane wywołanie *nie* nastąpiło —
walidator odrzuci plik:

```text
class "denied" requires at least one absence assertion (tool_not_called or
event_not_emitted) — a refusal asserted without asserting the absence of the
attempted call is half a test
```

Agent, który grzecznie odmawia i mimo to wywołuje narzędzie, przeszedłby tę drugą
połowę. Mniej więcej co piąta asercja w tym korpusie zapewnia, że coś **nie**
zaszło, a proporcja jest wymuszana, nie tylko postulowana.

## Czego się nauczyłeś

| | |
|---|---|
| Scenariusz to dane | Czytelny bez znajomości C#, przenośny do innego stosu |
| `why` liczy się tak samo jak `expect` | To po nim przyszły recenzent rozstrzyga, kto się myli |
| Najpierw czerwony, potem zielony | Niepowodzenie jest dowodem, że scenariusz cokolwiek mierzy |
| Test, który nigdy nie oblał, jest niesprawdzony | Zepsuj go celowo — repozytorium robi to formalnie |
| Odmowy wymagają dwóch asercji | „Odmówił" i „nie wywołał" to różne twierdzenia |

## Posprzątaj albo zostaw

Jeśli chcesz zachować swój scenariusz, potrzebuje wzmianki w
[`docs/SPEC.md` §3](../SPEC.md), żeby każdy scenariusz prowadził do jakiegoś
zadeklarowanego zachowania — zobacz
[Dodanie scenariusza](../how-to/add-a-scenario.pl.md). W przeciwnym razie:

```bash
rm evals/scenarios/happy/hap-009-single-day-vacation-thursday.yaml
```

## Dokąd dalej

- [Dodanie scenariusza](../how-to/add-a-scenario.pl.md) — pełna lista kontrolna, wraz z tym, co ten samouczek pominął
- [Dodanie zachowania](../how-to/add-a-behaviour.pl.md) — gdy agent naprawdę jeszcze czegoś nie potrafi
- [Diagnozowanie nieprzechodzącego scenariusza](../how-to/debug-a-failing-scenario.pl.md) — gdy rozbieżność nie jest oczywista
- [`DIAGRAMS.md` C1–C2](../DIAGRAMS.md) — pętla pomiaru i to, co faktycznie czyta asercja
