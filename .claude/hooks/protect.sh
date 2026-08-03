#!/bin/bash
# 旧フック。ガードレールは .claude/hooks/guard.py に統合された。
# 過去の設定（settings.local.json 等）がこのパスを参照していても動くよう、そのまま委譲する。
exec python3 "$(dirname "$0")/guard.py"
