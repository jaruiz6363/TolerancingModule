#!/usr/bin/env bash
# Reruns the checks in docs/verification.md and writes each command's output to <out-dir>.
#
#   verification/run.sh <out-dir> [lens-dir ...]
#
# Every lens-dir given is added to the sweep (section 7): every .zmx and .lhlt in it.
# The checks trace thousands of lenses; on 16 cores they take about an hour.
set -u
here=$(cd "$(dirname "$0")" && pwd)
root=$(dirname "$here")
out=${1:?usage: verification/run.sh <out-dir> [lens-dir ...]}
shift
mkdir -p "$out"

dotnet build "$root/src/Tolerancing.Cli" -c Release -nologo -v q > "$out/build.txt" 2>&1 || { cat "$out/build.txt"; exit 1; }
dll="$root/src/Tolerancing.Cli/bin/Release/net8.0/tol.dll"
triplet="$root/tests/Tolerancing.Tests/TestData/cooke_triplet.zmx"
quiet="--no-check --tree none"

# One check: its output to <out>/<name>.txt, with the command and the time it took.
check() {
    local name=$1; shift
    local start=$SECONDS
    { echo "\$ tol $*"; dotnet "$dll" "$@"; echo "[exit $?, $((SECONDS - start)) s]"; } > "$out/$name.txt" 2>&1
    echo "$name: $((SECONDS - start)) s"
}

# 1. Koch against traced lenses: the commercial set, and a set with biases and its own distributions.
check koch_commercial statistics "$triplet" --cross all --montecarlo 4000 --seed 11 $quiet
cat > "$out/biased.tol" <<'EOF'
TFRN 3 5
TFRN 4 -3 7 normal:3
TTHI 2 -0.02 0.1
TTHI 4 0.0 0.1 normal:2
TTHI 5 -0.05 0.05
TIND 3 0.001 normal:3
TEDX 3-4 0.02 endpoint
TEDY 3-4 0.02 endpoint
TSTX 3 0.03
TSTY 3 0.03
EOF
check koch_biased statistics "$triplet" --tolerances "$out/biased.tol" --cross all --montecarlo 4000 --seed 21 $quiet
for shape in pearson gamma normal; do
    check shape_commercial_$shape statistics "$triplet" --cross all --shape $shape $quiet
    check shape_biased_$shape statistics "$triplet" --tolerances "$out/biased.tol" --cross all --shape $shape $quiet
done

# 1b. Koch's model in W against the model in W², both from the same traced data, against traced lenses (section 3.3).
dotnet build "$here/WModel" -c Release -nologo -v q > "$out/build_wmodel.txt" 2>&1 || { cat "$out/build_wmodel.txt"; exit 1; }
wmodel="$here/WModel/bin/Release/net8.0/WModel.dll"
tests="$root/tests/Tolerancing.Tests/TestData"
for run in "triplet $triplet -" "triplet_allocated $triplet $root/examples/tolerances/cooke_triplet.tol" \
           "singlet $tests/biconvex_singlet.zmx -" "doublet $tests/cemented_doublet.zmx -" \
           "cassegrain $here/lenses/cassegrain.zmx -" "vignetting_triplet $here/lenses/vignetting_triplet.zmx -"; do
    read -r case file set <<< "$run"          # not set --, which would drop the lens directories
    start=$SECONDS
    { echo "\$ WModel $file $set 4000 3"; dotnet "$wmodel" "$file" "$set" 4000 3; echo "[exit $?, $((SECONDS - start)) s]"; } > "$out/wmodel_$case.txt" 2>&1
    echo "wmodel_$case: $((SECONDS - start)) s"
done

# 1c. The fields judged: the file's alone, each with its mirror image at -y (the default), and with its images on the x axis too.
check fields_file statistics "$triplet" --fields file --montecarlo 1000 --seed 3 $quiet
check fields_symmetric statistics "$triplet" --montecarlo 1000 --seed 3 $quiet
check fields_xy statistics "$triplet" --fields xy --montecarlo 1000 --seed 3 $quiet

# 1d. Compensators beside the focus: the last element's tilt and the image tilt, the last element's decenter, and the tilts again with xy fields, where the image tilts in both x and y.
check adjust_tilt statistics "$triplet" --adjust tilt:6-7 --adjust image-tilt --montecarlo 1000 --seed 3 $quiet
check adjust_decenter statistics "$triplet" --adjust decenter:6-7 --montecarlo 1000 --seed 3 $quiet
check adjust_tilt_xy statistics "$triplet" --fields xy --adjust tilt:6-7 --adjust image-tilt --montecarlo 1000 --seed 3 $quiet

# 2. Allocation to 2 waves, built: equal effect and least cost, and least cost within ±0.5 mm of focus.
for method in equal cost; do
    check allocate_$method allocate "$triplet" --limit 2 --method $method --write-tolerances "$out/allocate_$method.tol" $quiet
    check built_$method statistics "$triplet" --tolerances "$out/allocate_$method.tol" --limit 2 --montecarlo 1000 --seed 5 $quiet
done
check allocate_travel allocate "$triplet" --limit 2 --method cost --travel 0.5 --write-tolerances "$out/allocate_travel.tol" $quiet
check built_travel statistics "$triplet" --tolerances "$out/allocate_travel.tol" --limit 2 --travel 0.5 --montecarlo 1000 --seed 5 $quiet

# 3. Departures: EFL and boresight (at the image, in mm) against traced lenses.
check efl statistics "$triplet" --criterion efl --montecarlo 4000 --seed 3 $quiet
check boresight statistics "$triplet" --criterion boresight-mm --montecarlo 4000 --seed 3 $quiet

# 4. Several criteria: each at 97.7 %, then 97.7 % within all at once.
also="--also efl:0.25 --also boresight-mm:0.2"
check allocate_three allocate "$triplet" --limit 2.2 $also --method cost --write-tolerances "$out/allocate_three.tol" $quiet
check built_three statistics "$triplet" --tolerances "$out/allocate_three.tol" --limit 2.2 $also --montecarlo 1000 --seed 7 $quiet
for method in equal cost; do
    check allocate_joint_$method allocate "$triplet" --limit 2.2 $also --joint --method $method --write-tolerances "$out/allocate_joint_$method.tol" $quiet
    check built_joint_$method statistics "$triplet" --tolerances "$out/allocate_joint_$method.tol" --limit 2.2 $also --montecarlo 1000 --seed 7 $quiet
done

# 5. MTF floors: the spot synthesis against traced lenses (2000 each, one seed shared by the five), then a set allocated to an MTF, built.
vignetting="$here/lenses/vignetting_triplet.zmx"
check mtf_axis statistics "$triplet" --criterion mtf:0:20:t --limit 0.3 --montecarlo 2000 --seed 21 --tree none
check mtf_10t statistics "$triplet" --criterion mtf:10:10:t --limit 0.3 --montecarlo 2000 --seed 21 --tree none
check mtf_14s statistics "$triplet" --criterion mtf:14:10:s --limit 0.3 --montecarlo 2000 --seed 21 --tree none
check mtf_precision statistics "$triplet" --criterion mtf:0:20:t --grade precision --limit 0.5 --montecarlo 2000 --seed 21 --tree none
check mtf_vignetting statistics "$vignetting" --criterion mtf:14:10:t --limit 0.2 --montecarlo 2000 --seed 21 --tree none
check allocate_mtf allocate "$triplet" --criterion mtf:0:20:t --limit 0.3 --method cost --write-tolerances "$out/allocate_mtf.tol" $quiet
check built_mtf statistics "$triplet" --tolerances "$out/allocate_mtf.tol" --criterion mtf:0:20:t --limit 0.3 --montecarlo 1000 --seed 5 --tree none
check allocate_mtf_equal allocate "$triplet" --criterion mtf:0:20:t --limit 0.3 --method equal --write-tolerances "$out/allocate_mtf_equal.tol" $quiet
check built_mtf_equal statistics "$triplet" --tolerances "$out/allocate_mtf_equal.tol" --criterion mtf:0:20:t --limit 0.3 --montecarlo 1000 --seed 5 --tree none

# 6. The model check and the cross terms' share on every lens, at the commercial grade: the
#    default cross terms, from the wavefront slopes, against the traced ones of --cross auto.
lenses=("$root"/tests/Tolerancing.Tests/TestData/*.zmx "$here"/lenses/*.zmx)
for dir in "$@"; do
    for f in "$dir"/*.zmx "$dir"/*.lhlt; do [ -f "$f" ] && lenses+=("$f"); done
done
for lens in "${lenses[@]}"; do
    name=$(basename "$lens"); name=${name%.*}; name=${name// /_}
    check sweep_${name} statistics "$lens" --cross auto --tree none
    check sweep_${name}_wavefront statistics "$lens" --tree none
    # The fisheye, the lens farthest from quadratic, against its own traced lenses.
    if [ "$name" = FISHEYE160F2 ]; then check sweep_${name}_montecarlo statistics "$lens" --montecarlo 1000 --seed 3 --tree none; fi
done
echo "done: $out"
