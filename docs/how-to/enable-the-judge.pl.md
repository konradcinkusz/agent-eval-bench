# Włączenie sędziego

Warstwa 2 ocenia to, czego Warstwa 1 strukturalnie nie potrafi: czy odpowiedź jest
jasna, uczciwa, ugruntowana i we właściwym rejestrze. Bez danych dostępowych
raportuje `skipped:no-credential` — jawne pominięcie, nigdy ciche zielone.

> 🇬🇧 [English version](enable-the-judge.md) · ⬅ [Zacznij tutaj](../START-HERE.pl.md)

## Lokalnie

Ustaw trzy wartości. Lokalnie pochodzą z `dotnet user-secrets`, nigdy z pliku
w repozytorium:

```bash
cd src/AbsenceConcierge.AgentService
dotnet user-secrets set "Llm:Provider"   "AzureOpenAI"
dotnet user-secrets set "Llm:Endpoint"   "https://<twoj-zasob>.openai.azure.com"
dotnet user-secrets set "Llm:JudgeModel" "<nazwa-wdrozenia>"
dotnet user-secrets set "Llm:ApiKey"     "<klucz>"
```

**`Llm:Model` i `Llm:JudgeModel` to nazwy wdrożeń, a nie identyfikatory modeli.**
Pomylenie ich to typowy powód, dla którego pierwsze wywołanie Azure OpenAI nie
działa.

Następnie:

```bash
dotnet test --filter "FullyQualifiedName~Layer2"
```

## W CI

Nocny workflow czyta je ze środowiska GitHub `evals`:

| Zmienna | Do czego |
|---|---|
| `Llm__Provider` | `AzureOpenAI` |
| `Llm__Endpoint` | Endpoint zasobu |
| `Llm__JudgeModel` | Wdrożenie obsługujące sędziego |
| `Llm__ApiKey` | Klucz |
| `Llm__PricePerMillionInputTokens` | Opcjonalnie — pozwala raportowi podać koszt zamiast liczby tokenów |
| `Llm__PricePerMillionOutputTokens` | Jak wyżej |

`nightly.yml` działa o 02:30 UTC z zakresem `full`, a osobny test zapewnia, że tak
jest — przebieg z kluczem nie jest opcjonalny. W pull requestach zakres to `smoke`.

## Przypnij sędziego osobno od agenta

`Llm:JudgeModel` jest celowo oddzielone od `Llm:Model`. Gdyby oba ruszały się
razem, zmiany oceny nie dałoby się przypisać, bo obie strony porównania
poruszyłyby się naraz (ADR-0004).

Ten sam ADR zabrania cichego fallbacku: przebieg, który nie dosięgnął przypiętego
modelu i po cichu odpowiedział innym, zapisałby liczbę opisującą system, którego
nikt nie wybrał. Fallback jest dozwolony **wyłącznie** wtedy, gdy dostawca
raportuje model, który faktycznie odpowiedział, a wywołujący zapisuje to na
spanie — a wynik bazowy jest dzielony według modelu, który go wyprodukował.

## Zanim jego oceny będą mogły cokolwiek blokować

Najpierw kalibracja. Bramka jest zdefiniowana w `evals/rubrics/judge.yaml`:

| Wymóg | Wartość |
|---|---|
| Minimum etykiet | 40 |
| Minimum oznaczonych scenariuszy | 8 |
| Minimalna zgodność (kappa) | 0,6 |

Sędzia, którego nigdy nie porównano z człowiekiem, jest opinią z doklejoną liczbą.
Protokół i to, co znalazł jego pierwszy przebieg, są w
[`CALIBRATION.md`](../CALIBRATION.md) — samo etykietowanie wyprodukowało trzy
defekty ([F-9, F-10, F-11](../FINDINGS.md)), których żadne uruchomienie zestawu
wcześniej nie ujawniło, bo zmusiło kogoś do przeczytania każdego transkryptu przy
każdym zakotwiczeniu.

## Czego sędzia nie przyjmie

`RubricJudge.Parse` jest surowy, a każde odrzucenie jest raportowane jako
**awaria sędziego**, a nie jako niska ocena:

- proza zamiast obiektu JSON
- ocena poza skalą swojej rubryki
- brakujące kryterium — brak oceny to nie zero i nie zaliczenie
- kryterium, o które nikt nie prosił — sędzia wymyślający kryteria przestał
  podążać za plikiem rubryk, a to plik rubryk jest przypięciem
- ocena bez uzasadnienia — nieuzasadnionej oceny nie da się zrecenzować,
  a kalibracja jest recenzją

## Edycja rubryki

Zmiana `judge.yaml` albo `judge-prompt.md` zmienia przyrząd. Oba są hashowane
SHA-256 do każdego raportu, a sprawdzenie w CI odrzuca pull requesta, który je
zmienia bez podniesienia wersji zestawu — ocena porównywana przez taką edycję to
miarka, która zmieniła długość między odczytami.

## Koszt

`SPEC.md` §8.1 budżetuje Warstwę 2 zarówno w pieniądzu, jak i w minutach, a raport
zlicza tokeny wejściowe i wyjściowe na przebieg. Ustaw dwie zmienne cenowe, jeśli
chcesz, żeby raport podawał walutę zamiast tokenów.
