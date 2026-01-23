# Drone Swarm Task Allocation Results

## Summary

- **Run ID**: 20260123_222747
- **Method**: Classical (Topological Sort)
- **Tasks**: 8
- **Drones**: 4
- **Dependencies**: 7
- **Makespan**: 45.0 minutes
- **Resource Utilization**: 55.6%
- **Elapsed Time**: 84 ms

## Task Schedule

| Task | Start (min) | End (min) | Duration | Resource |
|------|-------------|-----------|----------|----------|
| T006 | 0.0 | 30.0 | 30.0 | unassigned |
| T001 | 0.0 | 5.0 | 5.0 | unassigned |
| T002 | 5.0 | 15.0 | 10.0 | payload_capacity |
| T003 | 5.0 | 15.0 | 10.0 | payload_capacity |
| T004 | 15.0 | 35.0 | 20.0 | payload_capacity |
| T005 | 15.0 | 30.0 | 15.0 | payload_capacity |
| T007 | 35.0 | 40.0 | 5.0 | payload_capacity |
| T008 | 40.0 | 45.0 | 5.0 | unassigned |

## Quantum Computing Context

This example demonstrates mapping drone task allocation to **Resource-Constrained Scheduling**:

- **Classical approach**: Topological sort + greedy assignment
- **Quantum approach**: QUBO encoding + QAOA optimization

Key constraints handled:
- **Precedence**: Tasks must respect dependency order
- **Resource capacity**: Drones have limited payload capacity
- **Makespan minimization**: Complete all tasks as quickly as possible

## Files Generated

- `metrics.json` - Performance metrics
- `schedule.csv` - Task assignments with timing
- `gantt.txt` - ASCII Gantt chart visualization
