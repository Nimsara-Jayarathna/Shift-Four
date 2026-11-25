#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
namespace ShiftFour.Tests
{
    public sealed partial class Student4UnifiedPlayModeTests
    {
        [UnityTest] public IEnumerator RenderedAllFour_ActualMotorTurningAndBraking()
        {
            string csv="agent,turn_seen,brake_seen,arrived,maximum_bank,maximum_pitch,travel_metres,target_speed_mps,measured_peak_mps,configured_accel_mps2,measured_peak_accel_mps2,configured_turn_deg_s,measured_peak_turn_deg_s,arrival_error_m,elapsed_s,replans\n";
            foreach(var brain in brains.OrderBy(b=>b.name))
            {
                var motor=brain.GetComponent(T("DroneMotor"));var agent=brain.GetComponent<NavMeshAgent>();
                var visual=brain.GetComponentInChildren(T("DroneRotorVisuals"));var body=brain.GetComponent(T("DroneBody"));
                Vector3 home=brain.transform.position;
                Assert.That(agent.Warp(new Vector3(3,0,4.5f)),Is.True);brain.transform.rotation=Quaternion.LookRotation(Vector3.right);
                yield return null;Physics.SyncTransforms();
                Assert.That((bool)Call(body,"CanOccupy",brain.transform.position),Is.True);
                Vector3 destination = new(3, 0, 15.5f);
                ((Behaviour)motor).enabled = true;
                Call(motor, "GoTo", destination);
                bool turned=false,braked=false;float bank=0,pitch=0,travel=0;float until=Time.time+14f;
                Vector3 previous = brain.transform.position;
                Quaternion previousHeading = brain.transform.rotation;
                float began = Time.time, previousAt = began, lastSpeed = 0f;
                float maxSpeed = 0f, maxAcceleration = 0f, maxTurn = 0f;
                while(Time.time<until)
                {
                    yield return null;
                    travel += Vector3.Distance(previous, brain.transform.position);
                    previous = brain.transform.position;
                    float speed = (float)Get(motor, "ActualSpeed");
                    float dt = Mathf.Max(.001f, Time.time - previousAt);
                    maxSpeed = Mathf.Max(maxSpeed, speed);
                    maxAcceleration = Mathf.Max(maxAcceleration, (speed - lastSpeed) / dt);
                    maxTurn = Mathf.Max(maxTurn, Quaternion.Angle(previousHeading, brain.transform.rotation) / dt);
                    previousAt = Time.time;
                    lastSpeed = speed;
                    previousHeading = brain.transform.rotation;
                    Assert.That((bool)Call(body,"CanOccupy",brain.transform.position),Is.True,brain.name+" movement clearance");
                    var pose=Field(visual,"pose");bank=Mathf.Max(bank,Mathf.Abs((float)Get(pose,"Bank")));pitch=Mathf.Max(pitch,Mathf.Abs((float)Get(pose,"Pitch")));
                    string state=Get(visual,"State").ToString();
                    if(!turned&&state=="Turning"&&bank>.1f){turned=true;yield return Capture(brain,brain.name+"-turning");}
                    if(!braked&&state=="Braking"){braked=true;yield return Capture(brain,brain.name+"-braking");}
                    if(Field(motor,"state").ToString()=="Arrived")break;
                }
                bool arrived=Field(motor,"state").ToString()=="Arrived";
                int replans = (int)motor.GetType().GetProperty("ReplanCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(motor);
                csv += FormattableString.Invariant($"{brain.name},{turned},{braked},{arrived},{bank:F3},{pitch:F3},{travel:F3},{Get(motor, "TargetSpeed")},{maxSpeed:F3},{agent.acceleration:F3},{maxAcceleration:F3},{agent.angularSpeed:F3},{maxTurn:F3},{Vector3.Distance(brain.transform.position, destination):F3},{Time.time - began:F3},{replans}\n");
                Write("actual-motion-states.csv",csv);
                Assert.That(turned,Is.True,brain.name+" actual root turn must bank");
                Assert.That(braked,Is.True,brain.name+" actual arrival must brake");Assert.That(arrived,Is.True);
                Assert.That(travel,Is.InRange(9f,14f),"No repeated route loops");
                ((Behaviour)motor).enabled=false;agent.isStopped=true;agent.ResetPath();agent.Warp(home);
            }
        }
    }
}
#endif
