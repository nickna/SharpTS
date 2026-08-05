#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <diagnostic-file> <feature-context>" >&2
  exit 64
fi

diagnostic_file="$1"
feature_context="$2"

if ! grep -Fq 'SHARPTS007' "$diagnostic_file"; then
  echo "expected managed-build-required diagnostic SHARPTS007" >&2
  cat "$diagnostic_file" >&2
  exit 1
fi

if ! grep -Fq -- "$feature_context" "$diagnostic_file"; then
  echo "managed-build-required diagnostic omitted feature context: $feature_context" >&2
  cat "$diagnostic_file" >&2
  exit 1
fi
