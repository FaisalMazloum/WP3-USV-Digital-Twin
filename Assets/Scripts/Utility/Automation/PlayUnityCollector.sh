#!/bin/bash

SEED_FILE="/tmp/unity_seed.txt"

for SEED in {667..900}; do
    echo "=== Starting iteration with seed $SEED ==="
    
    # Write seed to file for Unity to read
    echo $SEED > $SEED_FILE
    
    # Get Unity window ID dynamically
    UNITY_WINDOW=$(wmctrl -l | grep "Unity - REMORA - Remora.*Vulkan" | awk '{print $1}')
    # UNITY_WINDOW=0x052001e5


    # Focus Unity and press play
    wmctrl -i -a $UNITY_WINDOW
    sleep 1
    xdotool key ctrl+p
    
    # Wait for iteration to complete (takes 365.5 seconds for one trial) + margin
    # sleep 420
    # sleep 380 # TIME USED FOR DEGRADATION SIMULATION (WORKS PERFECTLY)
    sleep 260
    
    # Cleanup
    tmux kill-session -t remora 2>/dev/null
    pkill -f "remora_controller"
    pkill -f "ros2 topic pub"
    pkill -9 -f "ros2-daemon"
    
    echo "=== Iteration with seed $SEED complete ==="
    sleep 5
done

echo "All iterations complete!"