using System.Collections.Generic;
using ModernDrone.Drone;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModernDrone.UI
{
    /// <summary>
    /// Step-by-step calibration of a joystick-class device (RC transmitter in USB mode, HOTAS...):
    /// records centre and range of every axis, then detects which axis is throttle / roll / pitch / yaw /
    /// arm switch and its direction from the pilot's movements.
    /// </summary>
    public sealed class CalibrationWizard
    {
        enum Step { Center, Range, Throttle, Roll, Pitch, Yaw, Arm, Done }

        const float DetectThreshold = 0.35f;

        static readonly string[] Instructions =
        {
            "Отпустите стики в центр, ГАЗ — ВНИЗ до упора, тумблер арма — в положение DISARM.\nЗатем «Далее».",
            "Покрутите оба стика по кругу до упоров, щёлкните всеми тумблерами.\nЗатем верните в исходное положение и «Далее».",
            "Поднимите ГАЗ до упора вверх и держите. «Далее».",
            "Верните газ вниз. Отклоните КРЕН ВПРАВО до упора и держите. «Далее».",
            "Отпустите крен. Отклоните ТАНГАЖ ОТ СЕБЯ (вперёд) до упора и держите. «Далее».",
            "Отпустите тангаж. Отклоните РЫСКАНИЕ ВПРАВО до упора и держите. «Далее».",
            "Отпустите рыскание. Переключите тумблер АРМА в положение ARM. «Далее».\nНет тумблера — «Пропустить», арм будет кнопкой.",
            "Готово. Проверьте оси на мониторе и нажмите «Сохранить»."
        };

        readonly Joystick _device;
        readonly List<AxisControl> _axes = new List<AxisControl>();
        readonly List<string> _paths = new List<string>();
        float[] _center, _min, _max;
        readonly HashSet<int> _assigned = new HashSet<int>();
        readonly ControlSettings _result;
        Step _step;
        string _error;

        public bool Finished { get; private set; }
        public bool Saved { get; private set; }
        public Joystick Device => _device;

        public CalibrationWizard(Joystick device)
        {
            _device = device;
            _result = ControlSettings.Current;
            foreach (var c in device.allControls)
            {
                // Real analog axes only: buttons derive from AxisControl, stick up/down/left/right are synthetic.
                if (!(c is AxisControl axis) || c is ButtonControl || c.synthetic || c.noisy) continue;
                if (c.parent is StickControl && (c.name == "up" || c.name == "down" || c.name == "left" || c.name == "right")) continue;
                _axes.Add(axis);
                _paths.Add(c.path.Substring(device.path.Length + 1));
            }
            _center = new float[_axes.Count];
            _min = new float[_axes.Count];
            _max = new float[_axes.Count];
        }

        public int AxisCount => _axes.Count;
        public string Instruction => Instructions[(int)_step];
        public string Error => _error;
        public bool CanSkip => _step == Step.Arm;
        public string StepLabel => $"Шаг {(int)_step + 1} из {Instructions.Length}";
        public bool IsLastStep => _step == Step.Done;

        public void Update()
        {
            if (_step != Step.Range) return;
            for (int i = 0; i < _axes.Count; i++)
            {
                float v = _axes[i].ReadUnprocessedValue();
                _min[i] = Mathf.Min(_min[i], v);
                _max[i] = Mathf.Max(_max[i], v);
            }
        }

        public void Next()
        {
            _error = null;
            switch (_step)
            {
                case Step.Center:
                    for (int i = 0; i < _axes.Count; i++)
                        _center[i] = _min[i] = _max[i] = _axes[i].ReadUnprocessedValue();
                    break;
                case Step.Range:
                    break;
                case Step.Throttle:
                    if (!Detect(out int t, out bool tInv)) return;
                    _result.jsThrottle = Calibration(t, tInv);
                    break;
                case Step.Roll:
                    if (!Detect(out int r, out bool rInv)) return;
                    _result.jsRoll = Calibration(r, rInv);
                    break;
                case Step.Pitch:
                    if (!Detect(out int p, out bool pInv)) return;
                    _result.jsPitch = Calibration(p, pInv);
                    break;
                case Step.Yaw:
                    if (!Detect(out int y, out bool yInv)) return;
                    _result.jsYaw = Calibration(y, yInv);
                    break;
                case Step.Arm:
                    if (!Detect(out int a, out bool aInv)) return;
                    _result.jsArm = Calibration(a, aInv);
                    _result.jsUseArmAxis = true;
                    break;
                case Step.Done:
                    _result.device = ControlDevice.Joystick;
                    _result.joystickName = _device.displayName;
                    ControlSettings.Save();
                    Saved = true;
                    Finished = true;
                    return;
            }
            _step++;
        }

        public void Skip()
        {
            if (_step != Step.Arm) return;
            _result.jsUseArmAxis = false;
            _step++;
        }

        public void Cancel() => Finished = true;

        /// <summary>Finds the unassigned axis moved furthest from centre (relative to its range).</summary>
        bool Detect(out int index, out bool inverted)
        {
            index = -1;
            inverted = false;
            float best = DetectThreshold;
            for (int i = 0; i < _axes.Count; i++)
            {
                if (_assigned.Contains(i)) continue;
                float range = Mathf.Max(1e-4f, _max[i] - _min[i]);
                float d = (_axes[i].ReadUnprocessedValue() - _center[i]) / range;
                if (Mathf.Abs(d) > best)
                {
                    best = Mathf.Abs(d);
                    index = i;
                    inverted = d < 0f;
                }
            }
            if (index < 0)
            {
                _error = "Движение не обнаружено — отклоните стик до упора.";
                return false;
            }
            _assigned.Add(index);
            return true;
        }

        AxisCalibration Calibration(int i, bool inverted) => new AxisCalibration(_paths[i])
        {
            min = _min[i],
            center = _center[i],
            max = _max[i],
            invert = inverted
        };

        /// <summary>Axis monitor rows: path, raw value and normalised value (-1..1 over the observed range).</summary>
        public IEnumerable<(string path, float raw, float normalized, bool assigned)> Monitor()
        {
            for (int i = 0; i < _axes.Count; i++)
            {
                float raw = _axes[i].ReadUnprocessedValue();
                float lo = Mathf.Min(_min[i], -1f), hi = Mathf.Max(_max[i], 1f);
                if (_step > Step.Range) { lo = _min[i]; hi = _max[i]; }
                float n = Mathf.Approximately(hi, lo) ? 0f : Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(lo, hi, raw));
                yield return (_paths[i], raw, n, _assigned.Contains(i));
            }
        }
    }
}
