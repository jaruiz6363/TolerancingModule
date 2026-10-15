#!/usr/bin/env bash
# Regenerates the examples in this folder: the user guide's typical session (section 1) on the
# in-repo Cooke triplet, with every report tol prints and every file it writes; then the same on an
# aspheric singlet, as far as its drawings with their sag tables.
#
#   examples/run.sh
#
# Takes a few minutes. On Windows, run it from Git Bash.
set -eu
here=$(cd "$(dirname "$0")" && pwd)
root=$(dirname "$here")
dotnet build "$root/src/Tolerancing.Cli" -c Release -nologo -v q > /dev/null
dll="$root/src/Tolerancing.Cli/bin/Release/net8.0/tol.dll"
lens="$root/tests/Tolerancing.Tests/TestData/cooke_triplet.zmx"
asphere="$root/verification/lenses/aspheric_singlet.zmx"

cd "$here"
rm -rf reports tolerances drawings settings plots
mkdir -p reports tolerances drawings settings plots

# One report: the command, then what it printed.
report() {
    local name=$1; shift
    { echo "\$ tol $*" | sed "s|$lens|cooke_triplet.zmx|; s|$asphere|aspheric_singlet.zmx|"; echo; dotnet "$dll" "$@"; } > "reports/$name.txt" 2>&1
    echo "reports/$name.txt"
}

report 1-nominal nominal "$lens"
report 2-sensitivity sensitivity "$lens" --top 15
report 3-allocate allocate "$lens" --limit 2 --method cost \
    --write-tolerances tolerances/cooke_triplet.tol --report reports/cooke_triplet.json
report 4-statistics statistics "$lens" --tolerances tolerances/cooke_triplet.tol --limit 2 --montecarlo 1000 --seed 5 --plots plots
report 5-several-criteria allocate "$lens" --limit 2.2 --also efl:0.25 --also boresight:0.2 --method cost \
    --write-tolerances tolerances/cooke_triplet_three_criteria.tol
report 6-mtf-floor statistics "$lens" --tolerances tolerances/cooke_triplet.tol --criterion mtf:0:20:t --limit 0.3

dotnet "$dll" settings settings/drawing-settings.json > /dev/null
report 7-drawings-iso drawings "$lens" --tolerances tolerances/cooke_triplet.tol --out drawings/iso
report 8-drawings-asme drawings "$lens" --tolerances tolerances/cooke_triplet.tol --out drawings/asme --standard asme

# The aspheric singlet: allocated to 0.17 waves, then edited by hand to add the asphere's rotationally
# invariant irregularity and conic, checked, and drawn.
report 9-asphere-allocate allocate "$asphere" --limit 0.17 --method cost --write-tolerances tolerances/aspheric_singlet.tol
{ cat tolerances/aspheric_singlet.tol
  echo "# added by hand: the asphere's zonal error and conic"
  echo "TRSI 1 0.5"
  echo "TCON 1 0.005"; } > tolerances/aspheric_singlet_edited.tol
report 10-asphere-statistics statistics "$asphere" --tolerances tolerances/aspheric_singlet_edited.tol --limit 0.17 --montecarlo 1000 --seed 5
report 11-asphere-drawings-iso drawings "$asphere" --tolerances tolerances/aspheric_singlet_edited.tol --out drawings/aspheric_singlet/iso
report 12-asphere-drawings-asme drawings "$asphere" --tolerances tolerances/aspheric_singlet_edited.tol --out drawings/aspheric_singlet/asme --standard asme

# Line ends as the repository keeps them: .NET on Windows writes CRLF.
sed -i 's/\r$//' reports/*.txt reports/*.json tolerances/*.tol settings/*.json
# The JSON report names the lens by its full path; keep only its name.
sed -i "s|$lens|cooke_triplet.zmx|g" reports/*.json
