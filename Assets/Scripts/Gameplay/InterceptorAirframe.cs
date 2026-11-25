using System.Collections.Generic;
using UnityEngine;

namespace ShiftFour
{
    // Five small mesh batches. DroneRotorVisuals owns their lifetime and animation clock.
    internal sealed class InterceptorAirframe
    {
        private static readonly Color Graphite = new Color32(37, 44, 51, 255);
        private static readonly Color Alloy = new Color32(89, 99, 109, 255);
        private static readonly Color Cyan = new Color32(0, 217, 255, 255);
        private static readonly Color Amber = new Color32(255, 170, 36, 255);
        private static readonly Color Red = new Color32(255, 52, 52, 255);
        private static readonly int Base = Shader.PropertyToID("_BaseColor");
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        private static readonly SharedDroneMaterial Finish = new(() =>
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Interceptor ceramic alloy", hideFlags = HideFlags.DontSave };
            m.SetFloat("_Metallic", .66f);
            m.SetFloat("_Smoothness", .59f);
            m.EnableKeyword("_EMISSION");
            return m;
        });
        internal Transform Root { get; }
        internal string SensorStatus => mode.ToString();
        private enum Mode { Standby, Investigate, Alerted, Intercept, Combat, Search, Return, Destroyed }
        private Mode mode;
        private readonly Transform optic;
        private readonly Renderer lens, engines, shoulderArmor;
        private float previousImpact;
        private readonly MaterialPropertyBlock block = new();
        private readonly List<Mesh> meshes = new();
        private Material material;
        private Color color = Cyan;
        private float thrust = .35f, yaw, scan;

        internal InterceptorAirframe(Transform model, List<Transform> rotors)
        {
            Root = new GameObject("Interceptor IV airframe").transform;
            Root.SetParent(model, false);
            material = Finish.Acquire();
            var armor = new Batch();
            var trim = new Batch();
            var lights = new Batch();
            // Swept, stepped shoulder plates, inset louvres and four bright unit stripes.
            for (int side = -1; side <= 1; side += 2)
            {
                float s = side;
                armor.Plate(new(s*.37f,.19f,.02f), .44f,.19f,1.06f, s*16f);
                trim.Plate(new(s*.37f,.29f,.06f), .32f,.045f,.83f,s*16f);
                armor.Plate(new(s*.38f,.317f,.07f), .26f,.026f,.71f,s*16f);
                for (int n = 0; n < 4; n++)
                    trim.Plate(new(s*(.36f+n*.012f),.334f,-.17f+n*.07f), .19f,.02f,.028f,s*16f);
                lights.Plate(new(s*.34f,.337f,.27f),.042f,.012f,.20f,s*16f);
            }
            foreach (var pivot in rotors)
            {
                var center = model.InverseTransformPoint(pivot.position);
                trim.Ring(center + Vector3.up*.075f,.22f,.175f);
                lights.Ring(center + Vector3.up*.078f,.202f,.187f);
            }
            shoulderArmor = Build("Swept armor", Root, armor, Graphite);
            optic = new GameObject("Scanning optical head").transform;
            optic.SetParent(Root,false);
            optic.localPosition = new(0,.10f,.72f);
            var housing = new Batch();
            housing.Plate(Vector3.zero,.55f,.22f,.38f,0);
            // Metallic brow on the fixed deck above the articulated optical head.
            trim.Plate(new(0,.234f,.73f),.58f,.065f,.39f,0);
            Build("Machined edges and fan shrouds", Root, trim, Alloy);
            Build("Optical housing",optic,housing,Graphite);
            engines = Build("Engine rings and running lights",Root,lights,Cyan);
            var glass = new Batch();
            glass.Plate(new(0,0,.19f),.26f,.092f,.027f,0);
            glass.Plate(new(.18f,0,.163f),.044f,.048f,.061f,0);
            lens = Build("Rangefinder lenses",optic,glass,Cyan);
            Paint(lens,Cyan,1.5f);
            Paint(engines,Cyan,thrust);
        }

        private Renderer Build(string name, Transform parent, Batch batch, Color tint)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent,false);
            var mesh = batch.Finish(name);
            meshes.Add(mesh);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            Paint(renderer,tint,0);
            return renderer;
        }

        private void Paint(Renderer renderer, Color tint, float intensity)
        {
            block.SetColor(Base,tint);
            block.SetColor(Emission,tint*intensity);
            renderer.SetPropertyBlock(block);
        }

        internal void Step(InterceptorBrain brain, DroneAnimationPose pose, float dt, bool dead, float failure)
        {
            var phase = brain.OutdoorPhase;
            var policy = brain.Policy;
            mode = dead ? Mode.Destroyed : phase switch
            {
                OutdoorInterceptorResponse.Phase.Alerted => Mode.Alerted,
                OutdoorInterceptorResponse.Phase.Attack => Mode.Combat,
                OutdoorInterceptorResponse.Phase.Reposition or OutdoorInterceptorResponse.Phase.Intercept => Mode.Intercept,
                OutdoorInterceptorResponse.Phase.Search => Mode.Search,
                OutdoorInterceptorResponse.Phase.Return => Mode.Return,
                OutdoorInterceptorResponse.Phase.Patrol => Mode.Standby,
                _ when brain.HasCommittedToAttack && brain.WeaponAimPoint.HasValue => Mode.Combat,
                _ when brain.IsInvestigatingSound => Mode.Investigate,
                _ => policy?.State switch
                {
                    InterceptorState.ObserveTarget or InterceptorState.PredictMovement => Mode.Investigate,
                    InterceptorState.InterceptAhead or InterceptorState.Replan => Mode.Intercept,
                    InterceptorState.PursueLastKnown or InterceptorState.SearchLastSeenArea => Mode.Search,
                    InterceptorState.ReturnHome => Mode.Return,
                    _ => Mode.Standby
                }
            };
            Vector3? focus = mode switch
            {
                Mode.Combat => brain.WeaponAimPoint,
                Mode.Intercept or Mode.Alerted => brain.DiagnosticMotor?.CurrentWaypoint,
                Mode.Search => brain.ReportedPosition,
                Mode.Investigate => brain.IsInvestigatingSound ? brain.HeardDestination : brain.WeaponAimPoint,
                _ => null
            };
            float bearing = 0f;
            if (focus.HasValue)
            {
                var dir = Root.InverseTransformPoint(focus.Value) - optic.localPosition;
                bearing = Mathf.Clamp(Mathf.Atan2(dir.x,dir.z)*Mathf.Rad2Deg,-32,32);
            }
            scan = Mathf.Repeat(scan+dt*(mode == Mode.Standby ? 1.5f : 2.6f),2*Mathf.PI);
            if (mode is Mode.Standby or Mode.Search or Mode.Investigate)
                bearing += Mathf.Sin(scan)*(mode == Mode.Standby ? 12f : 18f);
            yaw = Mathf.Lerp(yaw,bearing,1-Mathf.Exp(-7*dt));
            optic.localRotation = Quaternion.Euler(0,yaw,0);
            Color target = mode switch
            {
                Mode.Alerted or Mode.Combat => Red,
                Mode.Investigate or Mode.Intercept or Mode.Search => Amber,
                Mode.Destroyed => Color.black,
                _ => Cyan
            };
            float blend = 1-Mathf.Exp(-8*dt);
            color = Color.Lerp(color,target,blend);
            thrust = Mathf.Lerp(thrust,dead ? 0 : Mathf.Clamp(.28f + (pose.RotorSpeed-.66f)*2.6f,.16f,1.7f),blend);
            Paint(engines,Cyan * (dead ? 1-pose.ShutdownBlend : 1),thrust);
            Paint(lens,Color.Lerp(color,dead ? Color.white : Amber,dead ? failure : pose.HitFlash),1.55f);
            if (previousImpact != pose.HitFlash)
            {
                previousImpact = pose.HitFlash;
                Paint(shoulderArmor, Color.Lerp(Graphite, Amber, previousImpact*.6f), previousImpact*.22f);
            }
        }

        internal void Stop()
        {
            Paint(lens,Color.black,0);
            Paint(engines,Color.black,0);
        }

        internal void Dispose()
        {
            foreach (var mesh in meshes) Object.Destroy(mesh);
            Finish.Release(ref material);
        }

        // Flat faces preserve machined bevels; each group is one draw per material pass.
        private sealed class Batch
        {
            private readonly List<Vector3> vertices = new();
            private readonly List<int> indices = new();
            private void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
            {
                int i = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                indices.Add(i); indices.Add(i+1); indices.Add(i+2);
                indices.Add(i); indices.Add(i+2); indices.Add(i+3);
            }
            internal void Plate(Vector3 center,float w,float h,float l,float yaw)
            {
                var q = Quaternion.Euler(0,yaw,0);
                Vector3 P(float x,float y,float z) => center+q*new Vector3(x*w,y*h,z*l);
                Vector3[] lower = {P(-.5f,-.5f,-.5f),P(-.36f,-.5f,.5f),P(.36f,-.5f,.5f),P(.5f,-.5f,-.5f)};
                Vector3[] upper = {P(-.39f,.5f,-.45f),P(-.28f,.5f,.39f),P(.28f,.5f,.39f),P(.39f,.5f,-.45f)};
                Quad(upper[0],upper[1],upper[2],upper[3]);
                Quad(lower[3],lower[2],lower[1],lower[0]);
                for (int i=0;i<4;i++)
                {
                    int j=(i+1)%4;
                    Quad(lower[i],lower[j],upper[j],upper[i]);
                }
            }
            internal void Ring(Vector3 center,float outer,float inner)
            {
                for (int i=0;i<16;i++)
                {
                    Vector3 a=Quaternion.Euler(0,i*22.5f,0)*Vector3.forward;
                    Vector3 b=Quaternion.Euler(0,(i+1)*22.5f,0)*Vector3.forward;
                    Quad(center+a*inner,center+a*outer,center+b*outer,center+b*inner);
                }
            }
            internal Mesh Finish(string name)
            {
                var mesh=new Mesh {name=name};
                mesh.SetVertices(vertices); mesh.SetTriangles(indices,0); mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
