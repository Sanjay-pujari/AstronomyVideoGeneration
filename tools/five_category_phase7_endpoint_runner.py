#!/usr/bin/env python3
"""Run supplied production plan ids through phases 1-7 and print a benchmark matrix.

This helper intentionally contains no family-specific production logic. Event labels are
report-only; the endpoint remains authoritative for phase execution and diagnostics.
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class PlanRun:
    label: str
    plan_id: str


def post_json(url: str, payload: dict[str, Any], timeout: int) -> tuple[int, dict[str, Any]]:
    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"}, method="POST")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8")
            return resp.status, json.loads(body) if body else {}
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8")
        try:
            parsed = json.loads(body) if body else {}
        except json.JSONDecodeError:
            parsed = {"rawBody": body}
        return exc.code, parsed


def extract_paths(body: dict[str, Any]) -> dict[str, Any]:
    diagnostics = body.get("diagnostics") or body.get("Diagnostics") or {}
    return {
        "outputRoot": body.get("outputRoot") or body.get("OutputRoot"),
        "phase6ValidationPath": diagnostics.get("phase6ValidationPath") or diagnostics.get("Phase6ValidationPath"),
        "phase7ValidationPath": diagnostics.get("phase7ValidationPath") or diagnostics.get("Phase7ValidationPath"),
        "semanticDiagnosticsPaths": diagnostics.get("semanticDiagnosticsPaths") or diagnostics.get("SemanticDiagnosticsPaths") or [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Run real production endpoint benchmarks for supplied plan IDs through Phase 1-7.")
    parser.add_argument("--endpoint", required=True, help="Production execution endpoint URL")
    parser.add_argument("--plan", action="append", nargs=2, metavar=("LABEL", "PLAN_ID"), required=True, help="Report label and plan id; pass in benchmark order")
    parser.add_argument("--timeout", type=int, default=600)
    args = parser.parse_args()

    rows = []
    for label, plan_id in args.plan:
        payload = {"contentGenerationPlanId": plan_id, "requestedStartPhaseNo": 1, "requestedEndPhaseNo": 7, "dryRun": False}
        status, body = post_json(args.endpoint, payload, args.timeout)
        paths = extract_paths(body)
        rows.append({
            "event": label,
            "planId": plan_id,
            "httpStatus": status,
            "phaseResults": body.get("phaseResults") or body.get("phases") or body.get("Phases"),
            "topLevelErrors": body.get("errors") or body.get("Errors") or [],
            "lastCompletedPhase": body.get("lastCompletedPhase") or body.get("LastCompletedPhase"),
            "lastFailedPhase": body.get("lastFailedPhase") or body.get("LastFailedPhase"),
            **paths,
        })
    print(json.dumps({"phaseRange": "1-7", "runs": rows}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
