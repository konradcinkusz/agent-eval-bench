# Diagnozowanie nieprzechodzącego scenariusza

> 🇬🇧 [English version](debug-a-failing-scenario.md) · ⬅ [Zacznij tutaj](../START-HERE.pl.md)

## Najpierw odpowiedz na jedyne pytanie, które się liczy

**Myli się scenariusz czy agent?**

Przeczytaj pole `why` scenariusza, zanim przeczytasz jakikolwiek kod. To pole
istnieje właśnie na tę chwilę: zapisuje, w co wierzył autor i dlaczego. Jeśli
`why` nadal opisuje zachowanie, z którym się zgadzasz — myli się agent. Jeśli nie
— myli się scenariusz.

Nie pomijaj tego kroku. Zmienianie asercji, aż zacznie przechodzić, to sposób,
w jaki regresja zostaje zapisana jako oczekiwane zachowanie.

## Czytaj ślad, nie odpowiedź

Warstwa 1 asertuje nad spanami i zdarzeniami, nigdy nad prozą. Odpowiedź mówiąca
Ci, że coś się stało, nie jest dowodem, że się stało.

```bash
dotnet test --filter "FullyQualifiedName~Layer1"
```

Komunikat nazywa scenariusz, asercję i to, co ślad faktycznie zawierał.

## Niepowodzenia według kształtu

### `tool_called` oczekiwało N, dostało M

Agent wywołał narzędzie inną liczbę razy. Jeśli M jest większe, a narzędziem jest
`request_time_off` — zatrzymaj się i potraktuj to poważnie: to C-6, a znaczy, że
komuś urlop mógłby zostać zarezerwowany dwa razy.

Sprawdź, czy w grę wchodzi ponowienie. Ponowienie na poziomie orkiestratora
otwiera **drugi span**, a nie drugą próbę wewnątrz jednego spanu — to rozróżnienie
jest defektem [F-2](../FINDINGS.md), a pomylenie go pozwoliło ukryć się defektowi
podwójnego zapisu.

### `order` nie przeszło — zapis wyprzedził potwierdzenie

To C-1, warunek, wokół którego zbudowane jest całe repozytorium. Coś pozwoliło na
zapis bez zapisanej decyzji człowieka w tym samym śladzie. Sprawdź, czy Twój krok
nie został zarejestrowany przed `ConfirmationGateStep`
w `ServiceCollectionExtensions.cs`.

### `argument_grounded` nie przeszło

C-5: identyfikator użyty w zapisie nie pojawił się w żadnym wcześniejszym wyniku
narzędzia w tym samym śladzie. Albo agent go wymyślił — pewnie brzmiący
identyfikator typu urlopu to klasyczny przypadek — albo wynik narzędzia nigdy nie
został zapisany.

Sprawdź, czy `workforce.tool.result_ids` jest obecne na spanie odczytu. Jeśli jest
puste, problemem jest opomiarowanie, a nie agent. To był
[F-4](../FINDINGS.md): warunek był wyspecyfikowany i niesprawdzalny, bo nic nie
zapisywało, co narzędzie zwróciło.

### `output_excludes_internal_ids` nie przeszło

C-3: wewnętrzny identyfikator dotarł do tekstu widzianego przez użytkownika. Zwykle
szablon odpowiedzi wstawiający coś, co powinien był nazwać.

### `termination` oczekiwało `decision`, dostało `iteration_cap`

C-4: tura skończyła się wyczerpaniem limitu kroków, a nie decyzją. Albo potok jest
dłuższy niż `MaxSteps` (32), albo któryś krok się zapętla.

### `termination` dostało `error`

Tura rzuciła wyjątkiem. Orkiestrator celowo go łapie, żeby tura nadal wyprodukowała
oceniony wynik — inaczej scenariusz zawiódłby z komunikatem „brak wyniku" zamiast
z powodem faktycznej awarii. Wyjątek jest w logu.

### Wszystko nie przechodzi naraz

Sprawdź strefę czasową. Serwis odmawia startu bez `Europe/Madrid` i robi to
celowo, zamiast cofać się do UTC.

## Gdy daty się nie zgadzają

Sprawdź najpierw przypięty zegar scenariusza i to, jaki jest dzień tygodnia.
Większość niepowodzeń datowych to arytmetyka scenariusza, a nie agenta:

- Czy oczekiwana data jest policzona z `fixture.clock`, w `fixture.timezone`?
- Dla jednego dnia: czy `start_date` i `end_date` są takie same? Tu mieszka błąd
  o jeden.
- Czy zakres przekracza weekend, święto, granicę miesiąca albo zmianę czasu?
  Każdy z tych przypadków ma własny scenariusz w `ambiguity/` — porównaj z tym,
  który już przechodzi.
- Czy fraza jest naprawdę dwuznaczna? „Next Friday" powiedziane w piątek musi
  wyprodukować pytanie doprecyzowujące, a nie rozwiązaną datę. Zapewnianie tam
  daty jest zapewnianiem złego zachowania.

## Gdy sędzia zawodzi, zamiast wystawić niską ocenę

Nieczytelny werdykt jest raportowany jako **awaria sędziego** — to inny fakt niż
niska ocena i nigdy nie jest uśredniany jako zero. Parser odrzuca prozę zamiast
JSON-a, ocenę poza skalą rubryki, brakujące kryterium, kryterium, o które nikt nie
prosił, oraz ocenę bez uzasadnienia. Komunikat mówi które.

## Odizoluj to

`ScenarioRunner` daje każdemu scenariuszowi świeży kontener DI, świeży magazyn
tokenów, świeży magazyn rozmów i świat odbudowany z pliku testowego. Nic nie
przeżywa między scenariuszami, więc niepowodzenie pojawiające się tylko w pełnym
przebiegu, a nie pojedynczo, jest błędem wartym zgłoszenia, a nie migotaniem do
ponownego uruchomienia.

Projekt testów jednostkowych działa z wyłączoną równoległością z pokrewnego powodu:
`ActivitySource` jest globalny dla procesu, a równoległe testy podbierały sobie
nawzajem spany. To był [F-6](../FINDINGS.md) — trzy testy „zepsuły się" przy
commicie, który żadnego z nich nie dotykał.
