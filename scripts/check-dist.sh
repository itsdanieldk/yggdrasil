#!/usr/bin/env bash
# Sanity checks on a generated site: pages, CSS, JS, OG cards and feeds all made it into the output.
# CI runs it on the generate job's dist/, and again on .vercel/output/static, which is what actually ships.
#
# Usage: scripts/check-dist.sh [dir]    (default: dist)
set -euo pipefail

dir="${1:-dist}"
fail() { echo "::error::$1"; exit 1; }

[ -f "$dir/index.html" ] || fail "$dir/index.html missing"
[ -f "$dir/404.html" ]   || fail "$dir/404.html missing"

html=$(find "$dir" -name '*.html' | wc -l | tr -d ' ')
[ "$html" -ge 15 ] || fail "expected at least 15 HTML files in $dir, found $html"

css=$(wc -c < "$dir/assets/css/app.css" | tr -d ' ')
[ "$css" -gt 20000 ] || fail "app.css is only ${css} bytes; Tailwind likely did not scan the views"

js=$(wc -c < "$dir/assets/js/app.js" | tr -d ' ')
[ "$js" -gt 0 ] || fail "app.js is empty; esbuild did not run"

cards=$(find "$dir/og" -name '*.png' | wc -l | tr -d ' ')
[ "$cards" -ge 3 ] || fail "expected at least 3 OG cards in $dir, found $cards"

for feed in rss.xml sitemap-0.xml sitemap-index.xml; do
  python3 -c "import sys, xml.etree.ElementTree as E; E.parse(sys.argv[1])" "$dir/$feed" \
    || fail "$dir/$feed is not well-formed XML"
done

echo "ok: ${html} HTML files, ${cards} OG cards, ${css} bytes CSS, ${js} bytes JS in $dir"
