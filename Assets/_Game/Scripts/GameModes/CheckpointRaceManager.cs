using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainDrive
{
    /// <summary>
    /// Ведёт последовательность чекпоинтов: какой следующий, сколько пройдено, круги, финиш.
    /// Режим anyOrder — точки берутся в любом порядке (исследование в Free Ride).
    /// Вешается на пустой объект «Checkpoints», чекпоинты — его дочерние объекты.
    /// </summary>
    public class CheckpointRaceManager : MonoBehaviour
    {
        [Tooltip("Чекпоинты по порядку. Если пусто — берутся дочерние объекты по порядку в иерархии.")]
        public List<Checkpoint> checkpoints = new List<Checkpoint>();
        [Min(1)] public int laps = 1;
        [Tooltip("Проходить точки в любом порядке.")]
        public bool anyOrder;
        [Tooltip("Показывать только текущую и следующую точку (меньше визуального шума).")]
        public bool showOnlyNext = true;

        /// <summary>(пройдено, всего)</summary>
        public event Action<int, int> CheckpointPassed;
        public event Action<Checkpoint> WrongCheckpoint;
        public event Action Finished;

        public int Passed { get; private set; }
        public int Total => anyOrder ? checkpoints.Count : checkpoints.Count * Mathf.Max(1, laps);
        public int CurrentLap { get; private set; }
        public bool IsRunning { get; private set; }
        public Checkpoint NextCheckpoint => IsRunning && nextIndex < checkpoints.Count && !anyOrder ? checkpoints[nextIndex] : null;
        public Checkpoint LastPassed { get; private set; }

        private int nextIndex;
        private readonly HashSet<Checkpoint> passedSet = new HashSet<Checkpoint>();
        private float lastWrongTime = -10f;

        private void Awake()
        {
            if (checkpoints.Count == 0)
                checkpoints.AddRange(GetComponentsInChildren<Checkpoint>(true));
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i] == null) continue;
                checkpoints[i].manager = this;
                checkpoints[i].index = i;
            }
            SetAllHidden();
        }

        public void SetAllHidden()
        {
            foreach (var cp in checkpoints)
                if (cp != null) cp.SetState(CheckpointState.Hidden);
        }

        public void Begin()
        {
            Passed = 0;
            CurrentLap = 0;
            nextIndex = 0;
            passedSet.Clear();
            LastPassed = null;
            IsRunning = checkpoints.Count > 0;
            RefreshVisuals();
            CheckpointPassed?.Invoke(Passed, Total);
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void OnCheckpointEntered(Checkpoint cp, VehicleController vehicle)
        {
            if (!IsRunning) return;

            if (anyOrder)
            {
                if (passedSet.Contains(cp)) return;
                passedSet.Add(cp);
                Register(cp, vehicle);
                if (passedSet.Count >= checkpoints.Count) Finish();
                else RefreshVisuals();
                return;
            }

            if (cp != checkpoints[nextIndex])
            {
                // Не та точка: подсказываем не чаще раза в 2 секунды
                if (cp != LastPassed && Time.time - lastWrongTime > 2f)
                {
                    lastWrongTime = Time.time;
                    WrongCheckpoint?.Invoke(cp);
                }
                return;
            }

            Register(cp, vehicle);
            nextIndex++;
            if (nextIndex >= checkpoints.Count)
            {
                CurrentLap++;
                if (CurrentLap >= laps)
                {
                    Finish();
                    return;
                }
                nextIndex = 0;
            }
            RefreshVisuals();
        }

        private void Register(Checkpoint cp, VehicleController vehicle)
        {
            Passed++;
            LastPassed = cp;
            Transform r = cp.RespawnTransform;
            vehicle.SetSafePoint(r.position + Vector3.up * 0.5f, r.rotation);
            CheckpointPassed?.Invoke(Passed, Total);
        }

        private void Finish()
        {
            IsRunning = false;
            RefreshVisuals();
            Finished?.Invoke();
        }

        private void RefreshVisuals()
        {
            for (int i = 0; i < checkpoints.Count; i++)
            {
                var cp = checkpoints[i];
                if (cp == null) continue;

                if (anyOrder)
                {
                    cp.SetState(passedSet.Contains(cp) ? CheckpointState.Passed : CheckpointState.Active);
                    continue;
                }
                if (!IsRunning)
                {
                    cp.SetState(CheckpointState.Hidden);
                    continue;
                }

                int lookahead = (i - nextIndex + checkpoints.Count) % checkpoints.Count;
                bool lastLap = CurrentLap >= laps - 1;
                bool beyondFinish = lastLap && i < nextIndex;

                if (i == nextIndex) cp.SetState(CheckpointState.Active);
                else if (lookahead == 1 && !(lastLap && nextIndex == checkpoints.Count - 1)) cp.SetState(CheckpointState.Upcoming);
                else if (showOnlyNext || beyondFinish) cp.SetState(CheckpointState.Hidden);
                else cp.SetState(CheckpointState.Upcoming);
            }
        }
    }
}
