using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MuMech
{
    //When enabled, the ascent guidance module makes the purple navball target point
    //along the ascent path. The ascent path can be set via SetPath. The ascent guidance
    //module disables itself if the player selects a different target.
    public class MechJebModuleAscentGuidance : DisplayModule
    {
        public MechJebModuleAscentGuidance(MechJebCore core) : base(core) { }

        protected const string TARGET_NAME = "Ascent Path Guidance";

        public IAscentPath ascentPath = null;

        public EditableDouble desiredInclination = 0;

        public bool launchingToPlane = false;
        public bool launchingToRendezvous = false;

        MechJebModuleAscentAutopilot autopilot;

        public override void OnStart(PartModule.StartState state)
        {
            autopilot = core.GetComputerModule<MechJebModuleAscentAutopilot>();
            if(autopilot != null) desiredInclination = autopilot.desiredInclination;
        }

        public override void OnModuleEnabled()
        {
        }

        public override void OnModuleDisabled()
        {
            if (core.target.NormalTargetExists && (core.target.Name == TARGET_NAME)) core.target.Unset();
            launchingToPlane = false;
            launchingToRendezvous = false;
            MechJebModuleAscentPathEditor editor = core.GetComputerModule<MechJebModuleAscentPathEditor>();
            if (editor != null) editor.enabled = false;
        }

        public override void OnFixedUpdate()
        {
            if (ascentPath == null) return;

            if (core.target.Target != null && core.target.Name == TARGET_NAME)
            {
                double angle = Math.PI / 180 * ascentPath.FlightPathAngle(vesselState.altitudeASL);
                double heading = Math.PI / 180 * OrbitalManeuverCalculator.HeadingForInclination(desiredInclination, vesselState.latitude);
                Vector3d horizontalDir = Math.Cos(heading) * vesselState.north + Math.Sin(heading) * vesselState.east;
                Vector3d dir = Math.Cos(angle) * horizontalDir + Math.Sin(angle) * vesselState.up;
                core.target.UpdateDirectionTarget(dir);
            }
        }

        protected override void WindowGUI(int windowID)
        {
            GUILayout.BeginVertical();

            bool showingGuidance = (core.target.Target != null && core.target.Name == TARGET_NAME);

            if (showingGuidance)
            {
                GUILayout.Label("Фиолетовый круг - маркер направления");
                if (GUILayout.Button("Скрыть маркер направления")) core.target.Unset();
            }
            else if (GUILayout.Button("Показать маркер направления"))
            {
                core.target.SetDirectionTarget(TARGET_NAME);
            }

            if (autopilot != null)
            {
                if (autopilot.enabled)
                {
                    if (GUILayout.Button("Выключить автопилот")) autopilot.users.Remove(this);
                }
                else
                {
                    if (GUILayout.Button("Включить автопилот"))
                    {
                        autopilot.users.Add(this);
                    }
                }

                ascentPath = autopilot.ascentPath;

                GuiUtils.SimpleTextBox("Высота орбиты", autopilot.desiredOrbitAltitude, "км");
                autopilot.desiredInclination = desiredInclination;
            }

            GuiUtils.SimpleTextBox("Наклон орбиты", desiredInclination, "º");

            core.thrust.LimitToPreventOverheatsInfoItem();
            core.thrust.LimitToTerminalVelocityInfoItem();
            core.thrust.LimitAccelerationInfoItem();
            core.thrust.LimitThrottleInfoItem();
            autopilot.correctiveSteering = GUILayout.Toggle(autopilot.correctiveSteering, "Коррекция курса");

            autopilot.autostage = GUILayout.Toggle(autopilot.autostage, "Автостадии");
            if(autopilot.autostage) core.staging.AutostageSettingsInfoItem();

            core.node.autowarp = GUILayout.Toggle(core.node.autowarp, "Авто-время");

            if (autopilot != null && vessel.LandedOrSplashed)
            {
                if (core.target.NormalTargetExists)
                {
                    if (core.node.autowarp)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("До старта:", GUILayout.ExpandWidth(true));
                        autopilot.warpCountDown.text = GUILayout.TextField(autopilot.warpCountDown.text, GUILayout.Width(60));
                        GUILayout.Label("s", GUILayout.ExpandWidth(false));
                        GUILayout.EndHorizontal();
                    }
                    if (!launchingToPlane && !launchingToRendezvous)
                    {
                        GUILayout.BeginHorizontal();
                        if (GUILayout.Button("До рандеву:", GUILayout.ExpandWidth(false)))
                        {
                            launchingToRendezvous = true;
                        }
                        autopilot.launchPhaseAngle.text = GUILayout.TextField(autopilot.launchPhaseAngle.text, GUILayout.Width(60));
                        GUILayout.Label("º", GUILayout.ExpandWidth(false));
                        GUILayout.EndHorizontal();
                    }
                    if (!launchingToPlane && !launchingToRendezvous && GUILayout.Button("Старт в плоскость цели"))
                    {
                        launchingToPlane = true;
                    }
                }
                else
                {
                    launchingToPlane = launchingToRendezvous = false;
                    GUILayout.Label("Выберите цель для старта");
                }

                if (launchingToPlane || launchingToRendezvous)
                {
                    double tMinus;
                    if (launchingToPlane) tMinus = LaunchTiming.TimeToPlane(mainBody, vesselState.latitude, vesselState.longitude, core.target.Orbit);
                    else tMinus = LaunchTiming.TimeToPhaseAngle(autopilot.launchPhaseAngle, mainBody, vesselState.longitude, core.target.Orbit);

                    double launchTime = vesselState.time + tMinus;

                    if (autopilot.enabled && core.node.autowarp) core.warp.WarpToUT(launchTime - autopilot.warpCountDown);

                    if (launchingToPlane)
                    {
                        desiredInclination = core.target.Orbit.inclination;
                        desiredInclination *= Math.Sign(Vector3d.Dot(core.target.Orbit.SwappedOrbitNormal(), Vector3d.Cross(vesselState.CoM - mainBody.position, mainBody.transform.up)));
                    }                    

                    GUILayout.Label("Старт " + (launchingToPlane ? "в плоскость цели" : "в рандеву") + ": T-" + MuUtils.ToSI(tMinus, 0) + "s");
                    if (tMinus < 3 * vesselState.deltaT)
                    {
                        if (autopilot.enabled) Staging.ActivateNextStage();
                        launchingToPlane = launchingToRendezvous = false;
                    }

                    if (GUILayout.Button("Отмена")) launchingToPlane = launchingToRendezvous = false;
                }
            }

            if (autopilot != null && autopilot.enabled)
            {
                GUILayout.Label("Статус: " + autopilot.status);
            }

            MechJebModuleAscentPathEditor editor = core.GetComputerModule<MechJebModuleAscentPathEditor>();
            if (editor != null) editor.enabled = GUILayout.Toggle(editor.enabled, "Изменение траектории");

            GUILayout.EndVertical();

            base.WindowGUI(windowID);
        }

        public override GUILayoutOption[] WindowOptions()
        {
            return new GUILayoutOption[] { GUILayout.Width(240), GUILayout.Height(30) };
        }

        public override string GetName()
        {
            return "Ascent Guidance";
        }
    }
}
