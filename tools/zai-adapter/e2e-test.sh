#!/usr/bin/env bash
# Z.ai adapter end-to-end test — background-safe, log ke file, tidak menyentuh production.
APP="D:/MCP-Bridge/desktop-commander-local/app/publish-p1/MCP-Xellen.exe"
SANDBOX="D:/MCP-Bridge/tests/adapter"
LOG="D:/MCP-Bridge/logs/zai-adapter-e2e.txt"
cd "D:/MCP-Bridge/tools/zai-adapter"
{
  echo "=== ZAI ADAPTER E2E $(date '+%F %T') ==="
  mkdir -p "$SANDBOX"
  rm -f "$SANDBOX/stop.flag" "$SANDBOX/port.txt" "$SANDBOX/endpoint.txt" "$SANDBOX/token.txt"
  "$APP" --transport-demo &
  DEMO_PID=$!
  for i in $(seq 1 30); do
    [ -f "$SANDBOX/endpoint.txt" ] && break
    sleep 1
  done
  ENDPOINT=$(cat "$SANDBOX/endpoint.txt" 2>/dev/null)
  TOKENFILE="$SANDBOX/token.txt"
  echo "endpoint=$ENDPOINT"
  echo "--- healthz demo transport (GET) ---"
  curl -s -o /dev/null -w "%{http_code}\n" --max-time 3 "http://127.0.0.1:8399/healthz"
  echo "--- MODE LIST ---"
  python zai_mcp_client.py --endpoint "$ENDPOINT" --token-file "$TOKENFILE" --mode list
  echo "LIST_EXIT=$?"
  echo "--- MODE CALL (json) ---"
  python zai_mcp_client.py --endpoint "$ENDPOINT" --token-file "$TOKENFILE" --mode call --tool synthetic-adapter__echo --args-json '{"text":"zai-e2e-json"}'
  echo "CALL_EXIT=$?"
  echo "--- MODE CALL (forced SSE accept) ---"
  python zai_mcp_client.py --endpoint "$ENDPOINT" --token-file "$TOKENFILE" --mode call --tool synthetic-adapter__echo --args-json '{"text":"zai-e2e-sse"}' --sse
  echo "CALL_SSE_EXIT=$?"
  echo "--- NEGATIVE: wrong token (harus gagal, adapter tidak bocorkan token) ---"
  echo "wrongtoken-wrongtoken-wrongtoken" > "$SANDBOX/wrong-token.txt"
  python zai_mcp_client.py --endpoint "$ENDPOINT" --token-file "$SANDBOX/wrong-token.txt" --mode list
  echo "NEG_EXIT=$? (expect non-zero)"
  echo "stop" > "$SANDBOX/stop.flag"
  wait $DEMO_PID
  echo "=== DONE ==="
} > "$LOG" 2>&1
echo DONE
