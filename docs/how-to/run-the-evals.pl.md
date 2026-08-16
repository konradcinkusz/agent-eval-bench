# Uruchamianie ewaluacji

> 🇬🇧 [English version](run-the-evals.md) · ⬅ [Zacznij tutaj](../START-HERE.pl.md)

## Wszystko, tak jak robi to CI

```bash
dotnet test
```

Bez danych dostępowych, bez sieci, bez modelu. To ta sama komenda, którą uruchamia
bramka scalenia i bramka wydania — więc zielony wynik lokalnie znaczy dokładnie
to samo, co zielony wynik w CI.

## Tylko warstwa deterministyczna

Warstwa 1 asertuje nad śladem wykonania. To ta szybka.

```bash
dotnet test --filter "FullyQualifiedName~Layer1"
```

## Tylko przebieg mutacyjny

Uruchamia cztery celowo zepsute agenty i sprawdza, czy każdy zostaje złapany przez
scenariusz wskazany jako jego łapacz.

```bash
dotnet test --filter "FullyQualifiedName~MutationTests"
```

## Tylko sędzia

```bash
dotnet test --filter "FullyQualifiedName~Layer2"
```

Bez danych dostępowych każdy oceniany scenariusz raportuje
`skipped:no-credential`, a test jest **pominięty, nie zaliczony**. Żeby naprawdę
się uruchomił, zobacz [Włączenie sędziego](enable-the-judge.pl.md).

## Jeden scenariusz

Nazwy scenariuszy pojawiają się w wyjściu testów. Filtruj po identyfikatorze:

```bash
dotnet test --filter "FullyQualifiedName~Layer1"
```

a potem poszukaj identyfikatora w wyjściu. Nie ma filtra „na scenariusz", bo
scenariusze są danymi, a nie metodami testowymi.

## Walidacja korpusu bez uruchamiania agenta

Schemat, identyfikatory, nazwy plików, bramkowanie i dyscyplina asercji — bez
budowania:

```bash
npm run validate:scenarios
```

Używaj tego podczas pisania scenariusza. Jest znacznie szybsze niż `dotnet test`
i łapie każdy błąd strukturalny.

## Wszystko, co sprawdzają bramki dokumentacji

```bash
npm run lint
```

Uruchamia markdownlint, sprawdzenie linków względnych, sprawdzenie parowania
diagramów, walidator scenariuszy i walidator definicji agenta — czyli całe zadanie
CI `lint-docs`.

## Gdzie zapisywane są wyniki

| Ścieżka | Co zawiera |
|---|---|
| `TestResults/eval-report.json` | Pełny przebieg: wyniki per scenariusz, czasy, werdykty sędziego |
| `evals/baselines/layer1.json` | Zapisany wynik bazowy, z którym porównywane są scenariusze zachowań |

W pull requeście te same dane trafiają do jednego „przyklejonego" komentarza
niosącego różnicę względem wyniku bazowego — zamiast do dashboardu.

## Jak czytać bramki

| Klasa scenariusza | Reguła |
|---|---|
| bramkowany jako `constraint` | 100%. Każde niepowodzenie blokuje scalenie. Bez wyjątków i bez tłumaczenia „migotliwością" |
| bramkowany jako `behaviour` | Odsetek zdanych na poziomie zapisanego wyniku bazowego albo powyżej |
| Kryteria sędziego | Próg dla każdego kryterium; `grounding` ma dodatkowo podłogę, poniżej której nie może spaść żadna pojedyncza ocena |

## Jeśli cały zestaw wywala się natychmiast

Sprawdź najpierw strefę czasową. Serwis odmawia startu, jeśli `Europe/Madrid` nie
jest dostępna na maszynie — i robi to celowo, zamiast cofać się do UTC. Cofnięcie
się rozwiązywałoby każdą datę w złej ramie czasowej, podczas gdy wszystkie testy
nadal by przechodziły. Zainstaluj `tzdata`.
