# Dodanie zachowania

Gdy agent naprawdę jeszcze czegoś nie potrafi. Poniższa kolejność nie jest
sugestią — część z niej wymusza sprawdzenie w CI.

> 🇬🇧 [English version](add-a-behaviour.md) · ⬅ [Zacznij tutaj](../START-HERE.pl.md)

## Kolejność

```text
1. SPEC.md          powiedz, co agent MA robić, i zacytuj scenariusz, który to udowodni
2. scenariusz       napisz go; nie przechodzi, bo zachowanie nie istnieje
3. krok potoku      spraw, żeby przeszedł
4. wynik bazowy     zapisz na nowo i wstaw różnicę do pull requesta
```

„Najpierw specyfikacja" to metoda, a nie preferencja harmonogramu: prompt bywa
edytowany tak jak konfiguracja — od niechcenia — a to specyfikacja sprawia, że
taka edycja daje się zrecenzować.

## 1. Popraw specyfikację

Dodaj zachowanie do [`docs/SPEC.md`](../SPEC.md) §3 z kolejnym wolnym numerem
`B-`, jedną linijką, sprawdzalne, wskazując scenariusz, który je udowodni.

Jeśli to reguła o tym, czego agent **nigdy** nie może zrobić, jej miejsce jest
w §4 jako twardy warunek — a nowy twardy warunek potrzebuje scenariusza, który
bez niego nie przechodzi.

Podnieś wersję specyfikacji w nagłówku, dopisz wiersz do tabeli zmian mówiący co
i dlaczego się zmieniło, i podnieś `version` w
[`agents/absence-concierge/definition.json`](../../agents/absence-concierge/definition.json),
żeby się zgadzała. Walidator sprawdza, że jedna wersja występuje we wszystkich
trzech miejscach, w których jest zapisana.

## 2. Napisz scenariusz i zobacz, jak nie przechodzi

Zobacz [Dodanie scenariusza](add-a-scenario.pl.md). Musi nie przechodzić, zanim
napiszesz jakikolwiek kod — scenariusz, który przechodzi wobec agenta
niepotrafiącego danej rzeczy, to scenariusz, który niczego nie mierzy.

## 3. Dodaj krok, a nie instrukcję w prompcie

Zachowania mieszkają w potoku, nie w prozie. Utwórz klasę w
`src/AbsenceConcierge.AgentService/Agent/Steps/` implementującą `IAgentStep`:

```csharp
public string Name => "your_step_name";        // pojawia się w śladzie
public bool AppliesTo(AgentTurnContext context) => …;   // kiedy się uruchamia
public ValueTask<StepSignal> ExecuteAsync(…);  // Continue albo Stop
```

Zarejestruj go w `ServiceCollectionExtensions.cs` **na właściwej pozycji** —
kolejność potoku JEST specyfikacją, a kolejność rejestracji jest tą kolejnością.

Zapisz decyzję w śladzie, używając stałej z `AgentDiagnostics`. Jeśli Twoje
zachowanie potrzebuje nazwy, której ślad jeszcze nie ma — dodaj ją tam, pamiętając,
że zmiana nazwy czegokolwiek w tym pliku jest zmianą łamiącą kontrakt zestawu
ewaluacyjnego.

Dwie reguły, które wyłapują większość pierwszych podejść:

- **Odmowa należy przed odczytami.** `ScopeGuardStep` działa na pozycji 4, żeby
  odmowa nie kosztowała ani jednego wywołania narzędzia.
- **Kompozytor odpowiedzi nie jest krokiem.** Renderowanie nie jest decyzją
  i działa poza pętlą, więc każda ścieżka — również ta, która zatrzymała się
  wcześniej albo rzuciła wyjątkiem — nadal produkuje odpowiedź.

## 4. Zapisz wynik bazowy na nowo

Scenariusze bramkowane jako `behaviour` są mierzone względem
`evals/baselines/layer1.json`. Gdy Twój nowy scenariusz zacznie przechodzić, wynik
bazowy się przesuwa. Zapisz go na nowo i **wstaw różnicę do pull requesta** —
ręcznie edytowany wynik bazowy to sposób, w jaki regresja wchodzi jako
„oczekiwana zmiana", i właśnie dlatego `CODEOWNERS` wyodrębnia tę ścieżkę osobno.

## Czego CI nie przepuści

| Sprawdzenie | Reguła |
|---|---|
| `coupling` | Zmiana w `prompts/` albo `agents/` bez zmiany w `docs/SPEC.md` odrzuca pull requesta |
| `coupling` | Zmiana w `evals/fixtures/` albo `evals/rubrics/` bez podniesienia wersji odrzuca |
| `validate:agents` | Wersja definicji musi zgadzać się ze specyfikacją, we wszystkich trzech miejscach |
| `validate:agents` | `allowedTools` i `requireApproval` muszą zgadzać się z katalogiem odczytów i zapisów w kodzie serwisu |
| `architecture` | Słownictwo domenowe w `ServiceDefaults` powoduje błąd — jądro pozostaje instalacją wodno-kanalizacyjną |
| Warstwa 1 | Scenariusze warunków przy 100%, scenariusze zachowań na poziomie wyniku bazowego albo powyżej |

## Jeśli zachowanie potrzebuje modelu

Prawdopodobnie nie potrzebuje. Na ścieżce blokującej CI interpreter jest regułowy,
a model może napisać odpowiedź i nic poza tym — uruchamia się po tym, jak każda
decyzja została podjęta i zapisana.

Jeśli sięgasz po model, żeby podjął *decyzję*, przenosisz zachowanie z potoku do
prozy, gdzie żaden twardy warunek nie może go utrzymać. To dokładnie ta zmiana,
przeciwko której argumentuje całe to repozytorium.
