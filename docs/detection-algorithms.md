# Detection Algorithms

## Sliding window state per correlation key
- `ewmaErrorRate`
- `variance`
- `totalCount`
- `errorCount`
- observed hosts
- observed fingerprints

## Signals
- `z_error = (errorRate - ewmaErrorRate) / sqrt(variance)`
- `burst = currentRate / baselineRate`
- `novelty = 1 when fingerprint unseen`
- `hostSpread = 1 when host unseen in window`

## Final score

`score = normalize(0.4*z_error + 0.25*burst + 0.2*novelty + 0.15*hostSpread)`

Threshold default: `0.6`.
