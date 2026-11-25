# Student 04 · Phase F5 evidence

This package records the final OpenWorldPrototype Interceptor work and the shared Agent Controller checks. The report explains the scope, methods, results and limitations. [Final technical report](../../STUDENT04_FINAL_TECHNICAL_REPORT.md)

| Artifact | Contents |
| --- | --- |
| [Test results](TEST_RESULTS.md) | Unity suite counts, audit findings and archived baseline reproduction |
| [Performance results](PERFORMANCE_RESULTS.md) | Allocation, route validation and sampled live scene costs |
| [Prediction results](PREDICTION_RESULTS.csv) | Timestamped offline forecasts and matched measurements for seven scripted movement patterns |
| [Baseline comparison](BASELINE_COMPARISON.csv) | Original policy, current policy in ablation, and F4 adaptation enabled |
| [Coordination results](COORDINATION_RESULTS.csv) | Runtime fleet, assignments, slot and reservation observations |
| [Architecture](ARCHITECTURE.md) | Source map and chain of evidence |
| [Demonstration guide](DEMONSTRATION_GUIDE.md) | Repeatable Unity procedures and fixture controls |
| [Viva preparation](../../STUDENT04_VIVA_QUESTIONS.md) | Oral assessment questions and code-based answers |
| `test-results/` | Test Runner XML from original, audit and final runs |
| `screenshots/` | Unity-rendered frames. Capture provenance is in the demonstration guide. |
| `logs/` | Captured data and editor log excerpts |
| [File manifest](FILES.txt) | Full F5 source, tests, documents and evidence inventory |

The archived baseline is commit `eae136b7ecc3ef19677fdce55946f10cf30abd71`. Unity ran from disposable directories. The F5 source ran with the existing local F1–F4 changes applied. File hashes in `logs/source-sha256.txt` identify the locally integrated source reviewed for this report.
