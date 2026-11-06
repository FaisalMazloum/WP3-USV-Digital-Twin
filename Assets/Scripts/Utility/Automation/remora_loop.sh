#!/bin/bash

SESSION_NAME="${1:-remora_unity_session}"
tmux new-session -d -s "$SESSION_NAME" || tmux attach-session -t "$SESSION_NAME"

ROS_SETUP="source /opt/ros/jazzy/setup.bash"
# WS_SETUP="source /home/$USER/remora_ws/install/setup.bash"

# ====================================================================================================== #
#                   1. FIND THE WORKSPACE DIRECTORY AND SOURCE IT AUTOMATICALLY.                         #
#            2. IF CANNOT FIND, THEN THE FALLBACK USES HARDCODED PATH OF THE ROS2 WORKSPACE.             #
# ====================================================================================================== #
WS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../../.." && pwd)" # Method 1 (dynamic path)
[ -f "$WS_DIR/install/setup.bash" ] || WS_DIR="$HOME/WP3"             # Method 2 (harcoded path if M1 fails)
WS_SETUP="source $WS_DIR/install/setup.bash"
FULL_SETUP="$ROS_SETUP && $WS_SETUP"

SESSION="remora"

tmux kill-session -t $SESSION 2>/dev/null
pkill -f "remora_controller"
pkill -f "ros2 topic pub"

tmux new-session -d -s $SESSION

# --- PANE 0: TCP ENDPOINT ---
tmux send-keys -t $SESSION "$FULL_SETUP" C-m
# tmux send-keys -t $SESSION "sleep 10; echo '--- Starting TCP ---'; ros2 launch remora_controller tcp_endpoint.launch.py" C-m
# tmux send-keys -t $SESSION "sleep 10; echo '--- Starting TCP ---'; ros2 launch remora_controller tcp_endpoint.launch.py ip:=192.168.8.188" C-m
tmux send-keys -t $SESSION "sleep 0; echo '--- Starting TCP ---'; ros2 launch remora_controller tcp_endpoint.launch.py" C-m

# --- PANE 1: CONTROLLER (Right) ---
tmux split-window -h -t $SESSION
tmux send-keys -t $SESSION "$FULL_SETUP" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller.launch.py robot_count:=10 pattern:=adaptive_sampling_pattern" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller.launch.py robot_count:=10 pattern:=aggregation_pattern" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller.launch.py robot_count:=10 pattern:=dispersion_pattern" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller.launch.py robot_count:=10 pattern:=minimalist_flocking_pattern" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller_timed_switch.launch.py robot_count:=10 pattern_a:=dispersion_pattern pattern_b:=aggregation_pattern switch_duration:=30.0" C-m
# tmux send-keys -t $SESSION "sleep 15; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller_staggered_switch.launch.py robot_count:=10 pattern_a:=random_walk_pattern pattern_b:=aggregation_pattern switch_duration:=30.0 stagger_duration:=15.0" C-m
tmux send-keys -t $SESSION "sleep 0; echo '--- Starting Controller ---'; ros2 launch remora_controller swarm_controller_dynamic_switch.launch.py robot_count:=10 initial_pattern:=random_walk_pattern" C-m


# --- PANE 2: PUBLISHER (Bottom Left) ---
tmux select-pane -t 0
tmux split-window -v -t $SESSION
tmux send-keys -t $SESSION "$FULL_SETUP" C-m
tmux send-keys -t $SESSION "sleep 20; echo '--- Publishing ---'; ros2 topic pub /swarm_command communication_interfaces/msg/Int8Message '{data: 1}'" C-m

# --- PANE 3: MONITOR (Bottom Right) ---
tmux select-pane -t 1
tmux split-window -v -t $SESSION
tmux send-keys -t $SESSION "echo 'System Monitor Active'; bash" C-m

tmux attach-session -t $SESSION