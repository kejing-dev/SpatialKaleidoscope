#!/bin/bash
# Assemble the 10 per-scene JPEG frame folders into one H.264 demo with 0.6 s crossfades (like the app).
# Usage: tools/assemble_demo.sh <frames_dir> <out.mp4> [seconds_per_scene=4.2] [xfade=0.6] [fps=30]
# Frames come from: KALEIDO_DEMO_OUT=<frames_dir> tools/unity.sh Kaleido.Editor.KaleidoDemoVideo.Run demo-frames
# ffmpeg: any ffmpeg on PATH, else the static binary from `python3 -m pip install --user imageio-ffmpeg`.
set -eu
FR="$1"; OUT="$2"; D="${3:-4.2}"; X="${4:-0.6}"; FPS="${5:-30}"
FF=$(command -v ffmpeg || python3 -c "import imageio_ffmpeg;print(imageio_ffmpeg.get_ffmpeg_exe())")
dirs=($(ls -d "$FR"/scene*/ | sort)); n=${#dirs[@]}
args=(); filt=""
for i in $(seq 0 $((n-1))); do args+=(-framerate "$FPS" -i "${dirs[$i]}f%04d.jpg"); filt+="[$i:v]scale=in_range=full:out_range=limited:flags=lanczos,format=yuv420p,setsar=1,fps=$FPS[v$i];"; done
prev="v0"
for i in $(seq 1 $((n-1))); do off=$(python3 -c "print(round($i*($D-$X),3))"); filt+="[$prev][v$i]xfade=transition=fade:duration=$X:offset=$off[x$i];"; prev="x$i"; done
total=$(python3 -c "print(round($n*$D-($n-1)*$X,3))"); fo=$(python3 -c "print(round($total-0.5,3))")
filt+="[$prev]fade=t=in:st=0:d=0.5,fade=t=out:st=$fo:d=0.5[out]"
"$FF" -y -hide_banner -loglevel error "${args[@]}" -filter_complex "$filt" -map "[out]" -c:v libx264 -preset slow -crf 22 -pix_fmt yuv420p -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 -movflags +faststart -r "$FPS" -an "$OUT"
"$FF" -hide_banner -i "$OUT" 2>&1 | grep -E "Duration|Stream" | head -2
echo "expected total ${total}s"
