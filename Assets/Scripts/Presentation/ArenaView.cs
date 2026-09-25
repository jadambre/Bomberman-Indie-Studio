using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmberGrid
{
    /// <summary>Procedural, asset-free presentation. The simulation remains the authority for all positions.</summary>
    public sealed class ArenaView : MonoBehaviour
    {
        public Camera GameCamera { get; private set; }
        public bool ShakeEnabled = true;

        sealed class Theme
        {
            public Color Background, Floor, FloorAlt, Metal, Trim, Accent, Wood, WoodLight, Leaf;
            public Theme(string bg, string floor, string alternate, string metal, string trim, string accent, string wood, string woodLight, string leaf)
            { Background=C(bg); Floor=C(floor); FloorAlt=C(alternate); Metal=C(metal); Trim=C(trim); Accent=C(accent); Wood=C(wood); WoodLight=C(woodLight); Leaf=C(leaf); }
        }
        static readonly Theme[] Themes = {
            new Theme("172D32","3D6265","41696A","294A50","7BABA6","9CE8AB","B48650","E7BB73","6DA476"),
            new Theme("302833","9E7960","AC8669","66515A","BCA49A","FFD379","96603D","EAC78A","BF7862"),
            new Theme("1E304B","739CAD","80AABB","354F73","BDD7D9","85E9FA","547689","91BCD0","70A7C2"),
            new Theme("262342","584E79","625786","37354F","9087AC","F8A3CB","796275","C59F9B","9777BE"),
            new Theme("2D2630","5C4B51","69545A","362F39","9C8383","FF9B5F","86604D","DCA07B","B8695B")
        };

        sealed class ActorVisual
        {
            public GameObject Root;
            public Transform Body, LeftLeg, RightLeg, LeftArm, RightArm, Shield, Marker, Antenna;
            public float Angle;
        }
        sealed class BombVisual { public GameObject Root; public Transform Body, Fuse, Ring; }
        sealed class PickupVisual { public GameObject Root; public Transform Badge, Halo; }
        sealed class Mote { public Transform Transform; public Vector3 Origin; public float Phase; }
        sealed class Particle
        {
            public Transform Transform;
            public Vector3 Velocity, Spin;
            public float Remaining, Life, Size;
            public bool Gravity;
        }

        readonly Dictionary<string,Material> materials = new Dictionary<string,Material>();
        readonly Dictionary<int,ActorVisual> actors = new Dictionary<int,ActorVisual>();
        readonly Dictionary<int,BombVisual> bombs = new Dictionary<int,BombVisual>();
        readonly Dictionary<int,PickupVisual> pickups = new Dictionary<int,PickupVisual>();
        readonly Dictionary<int,GameObject> crates = new Dictionary<int,GameObject>();
        readonly Dictionary<int,Transform> flames = new Dictionary<int,Transform>();
        readonly List<Particle> particles = new List<Particle>();
        readonly List<Mote> motes = new List<Mote>();
        readonly List<Mesh> combinedMeshes = new List<Mesh>();
        readonly Dictionary<Material,Mesh> crateMeshes = new Dictionary<Material,Mesh>();
        readonly HashSet<int> liveKeys = new HashSet<int>();
        readonly List<int> staleKeys = new List<int>();
        readonly List<Mesh> primitiveMeshes = new List<Mesh>();
        Transform stageRoot, staticRoot, propsRoot, actorsRoot, effectsRoot, backdropRoot;
        Mesh boxMesh, sphereMesh, cylinderMesh, ringMesh, crystalMesh;
        Theme theme;
        Material dark, metal, trim, accent, floor, floorAlt, wood, woodLight, shadow, ivory;
        Light keyLight, fillLight;
        int width, height, stage;
        float clock, shake, cameraBlend;
        bool initialized;
        readonly System.Random decorativeRandom = new System.Random(82171);

        static Color C(string hex) { ColorUtility.TryParseHtmlString("#"+hex, out Color color); return color; }
        Vector3 Cell(float x, float y, float elevation=0) => new Vector3(x-(width-1)*.5f,elevation,y-(height-1)*.5f);

        public void Initialize(SessionState state)
        {
            if (initialized) { Rebuild(state); return; }
            initialized=true;
            boxMesh=BuildBeveledBox(.085f); primitiveMeshes.Add(boxMesh);
            ringMesh=BuildRing(.40f,.5f,48); primitiveMeshes.Add(ringMesh);
            crystalMesh=BuildCrystal(); primitiveMeshes.Add(crystalMesh);
            sphereMesh=PrimitiveMesh(PrimitiveType.Sphere);
            cylinderMesh=PrimitiveMesh(PrimitiveType.Cylinder);
            var cameraObject=new GameObject("Arena camera"); cameraObject.transform.SetParent(transform,false);
            GameCamera=cameraObject.AddComponent<Camera>(); GameCamera.tag="MainCamera";
            cameraObject.AddComponent<AudioListener>();
            GameCamera.orthographic=true; GameCamera.orthographicSize=7.8f;
            GameCamera.nearClipPlane=.1f; GameCamera.farClipPlane=150;
            GameCamera.allowHDR=true; GameCamera.allowMSAA=true;
            GameCamera.clearFlags=CameraClearFlags.SolidColor;
            GameCamera.transform.rotation=Quaternion.Euler(58,0,0);
            // A directional key produces readable contact shadows even without a render-pipeline package.
            keyLight=NewLight("Warm afternoon key",LightType.Directional,new Color(1,.94f,.83f),1.5f);
            keyLight.transform.rotation=Quaternion.Euler(48,-34,0);
            keyLight.shadows=LightShadows.Soft; keyLight.shadowStrength=.64f;
            keyLight.shadowBias=.035f; keyLight.shadowNormalBias=.18f;
            fillLight=NewLight("Cool rim",LightType.Directional,new Color(.58f,.76f,1),.38f);
            fillLight.transform.rotation=Quaternion.Euler(38,155,0);
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.65f,.73f,.83f);
            RenderSettings.ambientEquatorColor=new Color(.30f,.38f,.45f);
            RenderSettings.ambientGroundColor=new Color(.20f,.25f,.29f);
            RenderSettings.ambientIntensity=1;
            RenderSettings.fog=false;
            QualitySettings.shadows=ShadowQuality.All;
            QualitySettings.shadowResolution=ShadowResolution.High;
            QualitySettings.shadowDistance=55;
            QualitySettings.antiAliasing=4;
            Rebuild(state);
        }

        public void Rebuild(SessionState state)
        {
            if (!initialized) { Initialize(state); return; }
            if(stageRoot) Destroy(stageRoot.gameObject);
            foreach(var mesh in combinedMeshes) if(mesh) Destroy(mesh);
            combinedMeshes.Clear(); crateMeshes.Clear(); actors.Clear(); bombs.Clear(); pickups.Clear(); crates.Clear(); flames.Clear(); particles.Clear(); motes.Clear();
            width=state.Width; height=state.Height; stage=Mathf.Clamp(state.Stage,0,4); theme=Themes[stage];
            stageRoot=Group("Arena — "+Campaign.Names[stage],transform);
            staticRoot=Group("Batched architecture",stageRoot);
            propsRoot=Group("Destructible props",stageRoot);
            actorsRoot=Group("Characters",stageRoot);
            effectsRoot=Group("Effects",stageRoot);
            backdropRoot=Group("Atmosphere",stageRoot);
            dark=Mat("deep steel",C("172831"),.32f,.38f);
            metal=Mat("metal"+stage,theme.Metal,.25f,.4f);
            trim=Mat("trim"+stage,theme.Trim,.32f,.4f);
            accent=Mat("accent"+stage,theme.Accent,0,.3f,.4f);
            floor=Mat("floor"+stage,theme.Floor,.06f,.26f);
            floorAlt=Mat("floor alt"+stage,theme.FloorAlt,.06f,.26f);
            wood=Mat("crate"+stage,theme.Wood,.05f,.27f);
            woodLight=Mat("crate detail"+stage,theme.WoodLight,.05f,.35f);
            ivory=Mat("porcelain",C("F5EACF"),.16f,.56f);
            shadow=TransparentMat("contact shadows",new Color(.02f,.05f,.065f,.28f));
            GameCamera.backgroundColor=theme.Background;
            BuildArchitecture();
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int key=y*width+x;
                if(state.Tiles!=null && key<state.Tiles.Length)
                {
                    if((TileKind)state.Tiles[key]==TileKind.Pillar) BuildPillar(x,y);
                    if((TileKind)state.Tiles[key]==TileKind.Crate) crates.Add(key,BuildCrate(x,y));
                }
            }
            BatchStatic();
            BuildAtmosphere();
            Render(state,0,false);
        }

        public void Render(SessionState state,float dt,bool menu)
        {
            if(!initialized || !stageRoot) return;
            dt=Mathf.Min(dt,.08f); clock+=dt;
            cameraBlend=Mathf.MoveTowards(cameraBlend,menu?1:0,dt*2);
            float aspect=Mathf.Max(.5f,GameCamera.aspect);
            float ortho=Mathf.Max(7.35f,(width+3.5f)/(aspect*2));
            // Portrait and narrow windows still show the entire play field.
            ortho=Mathf.Lerp(ortho,Mathf.Max(10.3f,(width+2.5f)/(aspect*1.18f)),cameraBlend);
            GameCamera.orthographicSize=ortho;
            Vector3 aim=new Vector3(-Mathf.Min(5.5f,ortho*aspect*.36f)*cameraBlend,-.1f,Mathf.Lerp(.15f,-.65f,cameraBlend));
            Vector3 cameraPosition=aim-GameCamera.transform.forward*28;
            if(ShakeEnabled && shake>0)
                cameraPosition+=new Vector3(Mathf.Sin(clock*91),Mathf.Cos(clock*77),0)*shake*.10f;
            shake=Mathf.MoveTowards(shake,0,dt*3.4f);
            GameCamera.transform.position=cameraPosition;
            RenderCrates(state);
            RenderActors(state,dt,menu);
            RenderBombs(state);
            RenderPickups(state);
            RenderFlames(state);
            UpdateParticles(dt);
            for(int i=0;i<motes.Count;i++)
            {
                Mote m=motes[i];
                m.Transform.localPosition=m.Origin+new Vector3(Mathf.Sin(clock*.2f+m.Phase)*.5f,Mathf.Sin(clock*.43f+m.Phase)*.24f,Mathf.Cos(clock*.3f+m.Phase)*.5f);
                float size=.024f+(.5f+.5f*Mathf.Sin(clock*1.2f+m.Phase))*.019f;
                m.Transform.localScale=Vector3.one*size;
            }
        }

        public void HandleEvents(IReadOnlyList<GameEvent> events)
        {
            if(!initialized) return;
            for(int i=0;i<events.Count;i++)
            {
                GameEvent ev=events[i]; Vector3 point=Cell(ev.X,ev.Y,.3f);
                switch(ev.Kind)
                {
                    case GameEventKind.Exploded:
                        shake=Mathf.Min(1.2f,shake+.5f);
                        Burst(point,Mat("blast yellow",C("FFD781"),0,.4f,1),18,.06f,3.9f,.6f,false);
                        Burst(point,Mat("blast smoke",C("797D81"),0,.1f),7,.17f,1.4f,.8f,false); break;
                    case GameEventKind.CrateDestroyed:
                        Burst(point,woodLight,9,.11f,2.8f,.65f,true); break;
                    case GameEventKind.PickupTaken:
                        Burst(point+Vector3.up*.3f,PickupMaterial(ev.Pickup),16,.06f,2.3f,.62f,false); break;
                    case GameEventKind.ActorDied:
                        Burst(point+Vector3.up*.35f,ivory,14,.10f,3,.8f,true);
                        Burst(point,accent,9,.07f,2,.65f,false); shake=Mathf.Max(shake,.45f); break;
                    case GameEventKind.Footstep:
                        Burst(Cell(ev.X,ev.Y,.08f),Mat("dust"+stage,Color.Lerp(theme.Floor,theme.Trim,.4f),0,.1f),2,.04f,.25f,.2f,false); break;
                    case GameEventKind.StageWon:
                        for(int x=2;x<width-1;x+=3) Burst(Cell(x,height-2,1.2f),accent,20,.08f,4,1.6f,true); break;
                }
            }
        }

        void BuildArchitecture()
        {
            Material background=Mat("backdrop"+stage,theme.Background,0,.22f);
            Box("Endless matte backdrop",staticRoot,new Vector3(0,-1.48f,0),new Vector3(180,.15f,180),background);
            Box("Diorama foundation",staticRoot,new Vector3(0,-.84f,0),new Vector3(width+1.8f,.96f,height+1.8f),dark);
            Box("Copper reveal",staticRoot,new Vector3(0,-.37f,0),new Vector3(width+1.52f,.13f,height+1.52f),accent);
            Box("Chamfered arena deck",staticRoot,new Vector3(0,-.24f,0),new Vector3(width+1.6f,.30f,height+1.6f),metal);
            Box("Inner floor bed",staticRoot,new Vector3(0,-.105f,0),new Vector3(width+.2f,.19f,height+.2f),dark);
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                var p=Cell(x,y,-.018f);
                Box("Inlaid floor",staticRoot,p,new Vector3(.952f,.12f,.952f),(x+y)%2==0?floor:floorAlt);
                if((x+y)%3==0)
                    Box("Tile joint",staticRoot,p+new Vector3(.32f,.064f,.35f),new Vector3(.13f,.009f,.024f),trim);
            }
            // The inner corners read as deliberate launch pads and safe starting spaces.
            foreach(Vector2Int start in new[]{new Vector2Int(1,1),new Vector2Int(1,height-2),new Vector2Int(width-2,1),new Vector2Int(width-2,height-2)})
            {
                Vector3 p=Cell(start.x,start.y,.05f);
                Ring("Spawn pad",staticRoot,p,new Vector3(.77f,1,.77f),accent);
                Box("Pad inlay",staticRoot,p,new Vector3(.27f,.009f,.055f),accent);
            }
            float left=-width*.5f-.35f,right=width*.5f+.35f;
            for(int side=-1;side<=1;side+=2)
            {
                float z=side*(height*.5f+.32f);
                Box("Perimeter rail",staticRoot,new Vector3(0,.0f,z),new Vector3(width+.6f,.20f,.22f),trim);
                for(int x=0;x<width;x+=2)
                {
                    float wx=x-(width-1)*.5f;
                    Box("Deck marker",staticRoot,new Vector3(wx,.118f,z),new Vector3(.35f,.015f,.11f),accent);
                    Box("Rivet foot",staticRoot,new Vector3(wx,-.79f,z+side*.15f),new Vector3(.44f,.23f,.09f),metal);
                }
                float edgeX=side*(width*.5f+.32f);
                Box("Perimeter rail",staticRoot,new Vector3(edgeX,0,0),new Vector3(.22f,.2f,height+.7f),trim);
            }
            for(int sx=-1;sx<=1;sx+=2) for(int sz=-1;sz<=1;sz+=2)
            {
                Vector3 corner=new Vector3(sx*(width*.5f+.38f),.05f,sz*(height*.5f+.38f));
                Box("Corner housing",staticRoot,corner,new Vector3(.62f,.42f,.62f),metal);
                Box("Corner cap",staticRoot,corner+Vector3.up*.26f,new Vector3(.53f,.14f,.53f),trim);
                Box("Corner beacon",staticRoot,corner+Vector3.up*.35f,new Vector3(.26f,.10f,.26f),accent);
                BuildEnvironmentCluster(corner+new Vector3(sx*.9f,-.75f,sz*.9f),sx,sz);
            }
            // Deck-face technical details add depth without hiding the playable cells.
            for(int x=-5;x<=5;x+=5)
            {
                Vector3 p=new Vector3(x,-.82f,-height*.5f-.91f);
                Box("Front service panel",staticRoot,p,new Vector3(2.7f,.43f,.04f),metal);
                for(int n=0;n<6;n++) Box("Vent",staticRoot,p+new Vector3(-.8f+n*.28f,0,-.03f),new Vector3(.12f,.18f,.024f),dark);
                Box("Status lamp",staticRoot,p+new Vector3(1.08f,0,-.035f),new Vector3(.10f,.10f,.03f),accent);
            }
            // Ground details frame the diorama; the backdrop always extends beyond the camera.
            Material groundLine=Mat("ground line"+stage,Color.Lerp(theme.Background,theme.Trim,.12f),0,.1f);
            for(int i=-4;i<=4;i++)
                Box("Ground runway",staticRoot,new Vector3(i*4,-1.385f,0),new Vector3(.035f,.015f,70),groundLine);
            for(int i=-4;i<=4;i++)
                Box("Ground runway",staticRoot,new Vector3(0,-1.384f,i*4),new Vector3(70,.015f,.035f),groundLine);
        }

        void BuildPillar(int x,int y)
        {
            Vector3 p=Cell(x,y);
            bool border=x==0||y==0||x==width-1||y==height-1;
            float h=border?.62f:.72f;
            Box("Pillar footing",staticRoot,p+Vector3.up*.11f,new Vector3(.94f,.23f,.94f),dark);
            Box("Armored pillar",staticRoot,p+Vector3.up*(h*.5f+.1f),new Vector3(.82f,h,.82f),metal);
            Box("Pillar collar",staticRoot,p+Vector3.up*(h-.02f),new Vector3(.87f,.13f,.87f),trim);
            Box("Pillar inset lid",staticRoot,p+Vector3.up*(h+.065f),new Vector3(.67f,.09f,.67f),floorAlt);
            Box("Pillar front slot",staticRoot,p+new Vector3(0,.34f,-.414f),new Vector3(.38f,.10f,.018f),dark);
            Box("Pillar front lamp",staticRoot,p+new Vector3(-.12f,.34f,-.428f),new Vector3(.06f,.034f,.018f),accent);
            if(!border)
            {
                for(int sign=-1;sign<=1;sign+=2)
                    Box("Cap bolt",staticRoot,p+new Vector3(sign*.22f,h+.118f,-.22f),new Vector3(.075f,.025f,.075f),trim);
                if(stage==0 && (x+y)%4==0) BuildSprig(p+new Vector3(.25f,h+.12f,.18f),.48f);
                if(stage==2 && (x+y)%4==0) Crystal("Frost crystal",staticRoot,p+new Vector3(.2f,h+.24f,.15f),new Vector3(.12f,.25f,.12f),accent);
            }
        }

        GameObject BuildCrate(int x,int y)
        {
            Transform root=Group("Breakable crate",propsRoot); root.localPosition=Cell(x,y);
            if(crateMeshes.Count>0)
            {
                foreach(var part in crateMeshes)
                    Piece("Crate · "+part.Key.name,root,Vector3.zero,Vector3.one,part.Key,part.Value);
                return root.gameObject;
            }
            Box("Crate foot",root,new Vector3(0,.13f,0),new Vector3(.81f,.22f,.81f),dark);
            Box("Crate core",root,new Vector3(0,.43f,0),new Vector3(.80f,.61f,.80f),wood);
            Box("Crate lid",root,new Vector3(0,.78f,0),new Vector3(.84f,.11f,.84f),woodLight);
            for(int side=-1;side<=1;side+=2)
            {
                Box("Crate bands",root,new Vector3(side*.25f,.43f,-.412f),new Vector3(.075f,.56f,.043f),woodLight);
                Box("Crate lid bands",root,new Vector3(side*.25f,.846f,0),new Vector3(.075f,.028f,.73f),metal);
                Box("Side grip",root,new Vector3(side*.414f,.5f,0),new Vector3(.035f,.15f,.25f),dark);
            }
            Box("Crate front inset",root,new Vector3(0,.45f,-.421f),new Vector3(.24f,.22f,.018f),metal);
            Transform label=Box("Crate insignia",root,new Vector3(0,.45f,-.438f),new Vector3(.105f,.105f,.022f),woodLight);
            label.localRotation=Quaternion.Euler(0,0,45);
            // Every crate shares these four material meshes. Destroying one crate never duplicates or leaks its geometry.
            var groups=new Dictionary<Material,List<CombineInstance>>();
            var filters=root.GetComponentsInChildren<MeshFilter>();
            foreach(var filter in filters)
            {
                Material mat=filter.GetComponent<MeshRenderer>().sharedMaterial;
                if(!groups.TryGetValue(mat,out var list)) { list=new List<CombineInstance>();groups.Add(mat,list); }
                list.Add(new CombineInstance{mesh=filter.sharedMesh,transform=root.worldToLocalMatrix*filter.transform.localToWorldMatrix});
            }
            foreach(var filter in filters) Destroy(filter.gameObject);
            foreach(var pair in groups)
            {
                Mesh mesh=new Mesh{name="Shared crate · "+pair.Key.name};mesh.CombineMeshes(pair.Value.ToArray(),true,true);combinedMeshes.Add(mesh);crateMeshes.Add(pair.Key,mesh);
                Piece("Crate · "+pair.Key.name,root,Vector3.zero,Vector3.one,pair.Key,mesh);
            }
            return root.gameObject;
        }

        void BuildEnvironmentCluster(Vector3 p,int sx,int sz)
        {
            Material leaf=Mat("growth"+stage,theme.Leaf,0,.2f);
            Box("Exterior platform",staticRoot,p+new Vector3(0,-.35f,0),new Vector3(1.5f,.2f,1.25f),metal);
            if(stage==0)
            {
                Box("Planter",staticRoot,p,new Vector3(1.05f,.50f,.83f),floorAlt);
                Box("Soil",staticRoot,p+Vector3.up*.27f,new Vector3(.91f,.08f,.70f),dark);
                BuildSprig(p+new Vector3(-.20f,.32f,.1f),1.35f);
                BuildSprig(p+new Vector3(.25f,.32f,-.15f),.8f);
            }
            else if(stage==1)
            {
                for(int i=0;i<3;i++)
                {
                    Transform rock=Box("Sandstone boulder",staticRoot,p+new Vector3((i-1)*.42f,i*.08f,0),new Vector3(.7f,.5f+i*.15f,.6f),i%2==0?wood:woodLight);
                    rock.localRotation=Quaternion.Euler(0,i*29,12);
                }
                Cylinder("Excavation lamp",staticRoot,p+Vector3.up*.72f,new Vector3(.18f,.35f,.18f),metal);
                Sphere("Excavation light",staticRoot,p+Vector3.up*1.08f,Vector3.one*.22f,accent);
            }
            else if(stage==2)
            {
                for(int i=0;i<4;i++)
                {
                    Transform ice=Crystal("Ice spire",staticRoot,p+new Vector3((i-1.5f)*.28f,.20f+i*.08f,.12f*(i%2)),new Vector3(.35f,.8f+i*.24f,.35f),i%2==0?accent:trim);
                    ice.localRotation=Quaternion.Euler(0,i*33,(i-1.5f)*13);
                }
            }
            else if(stage==3)
            {
                Cylinder("Relay foot",staticRoot,p+Vector3.up*.05f,new Vector3(.8f,.2f,.8f),metal);
                Cylinder("Relay mast",staticRoot,p+Vector3.up*.60f,new Vector3(.13f,.55f,.13f),trim);
                Box("Relay transmitter",staticRoot,p+Vector3.up*1.12f,new Vector3(.61f,.26f,.61f),metal);
                Sphere("Relay pulse",staticRoot,p+Vector3.up*1.37f,Vector3.one*.20f,accent);
                for(int i=0;i<3;i++) Box("Heat sink",staticRoot,p+new Vector3((i-1)*.24f,.25f,0),new Vector3(.11f,.5f,.7f),leaf);
            }
            else
            {
                Cylinder("Core reactor",staticRoot,p+Vector3.up*.3f,new Vector3(.83f,.50f,.83f),metal);
                Cylinder("Reactor glow",staticRoot,p+Vector3.up*.35f,new Vector3(.87f,.20f,.87f),accent);
                Cylinder("Reactor crown",staticRoot,p+Vector3.up*.85f,new Vector3(.98f,.08f,.98f),trim);
                for(int i=0;i<4;i++)
                {
                    float a=i*Mathf.PI*.5f;
                    Box("Reactor strut",staticRoot,p+new Vector3(Mathf.Sin(a)*.4f,.36f,Mathf.Cos(a)*.4f),new Vector3(.1f,.9f,.1f),dark);
                }
            }
        }

        void BuildSprig(Vector3 p,float size)
        {
            Material leaves=Mat("growth"+stage,theme.Leaf,0,.2f);
            for(int i=0;i<3;i++)
            {
                Transform stem=Crystal("Succulent leaf",staticRoot,p+new Vector3((i-1)*.10f,.10f,0)*size,new Vector3(.16f,.40f,.13f)*size,leaves);
                stem.localRotation=Quaternion.Euler(i*19,(i-1)*80,(i-1)*32);
            }
        }

        void BuildAtmosphere()
        {
            for(int i=0;i<28;i++)
            {
                Vector3 p=new Vector3((float)decorativeRandom.NextDouble()*(width+5)-(width+5)*.5f,.3f+(float)decorativeRandom.NextDouble()*2.5f,(float)decorativeRandom.NextDouble()*(height+4)-(height+4)*.5f);
                Transform mote=Sphere("Drifting embers",backdropRoot,p,Vector3.one*.03f,accent);
                var renderer=mote.GetComponent<MeshRenderer>(); renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                motes.Add(new Mote{Transform=mote,Origin=p,Phase=i*1.831f});
            }
        }

        void RenderCrates(SessionState state)
        {
            staleKeys.Clear();
            foreach(var pair in crates)
                if(state.Tiles==null || pair.Key>=state.Tiles.Length || (TileKind)state.Tiles[pair.Key]!=TileKind.Crate) staleKeys.Add(pair.Key);
            foreach(int key in staleKeys) { Destroy(crates[key]); crates.Remove(key); }
        }

        ActorVisual BuildActor(ActorState actor)
        {
            Color color=actor.HumanIndex==0?C("81DCC7"):actor.HumanIndex==1?C("F6C46F"):
                actor.BotKind==0?C("E77A72"):actor.BotKind==1?C("AB91E5"):C("EC93B7");
            Material suit=Mat("suit "+ColorUtility.ToHtmlStringRGB(color),color,.2f,.53f);
            Material eye=Mat("visor light "+(actor.HumanIndex<0?"enemy":"friend"),actor.HumanIndex<0?C("FFB39D"):C("CBFFEE"),0,.4f,.7f);
            Transform root=Group(actor.HumanIndex<0?"Rival robot":"Player "+(actor.HumanIndex+1),actorsRoot);
            Cylinder("Contact shadow",root,new Vector3(0,.05f,0),new Vector3(.67f,.004f,.56f),shadow,false);
            Transform body=Group("Body bounce",root);
            Box("Pilot torso",body,new Vector3(0,.42f,0),new Vector3(.36f,.31f,.30f),suit);
            Box("Utility belt",body,new Vector3(0,.29f,0),new Vector3(.38f,.09f,.32f),dark);
            Box("Belt clasp",body,new Vector3(0,.30f,.18f),new Vector3(.12f,.065f,.035f),ivory);
            Box("Backpack",body,new Vector3(0,.43f,-.20f),new Vector3(.28f,.27f,.16f),dark);
            Box("Backpack glow",body,new Vector3(0,.45f,-.29f),new Vector3(.16f,.035f,.025f),accent);
            Sphere("Oversized helmet",body,new Vector3(0,.78f,0),new Vector3(.65f,.59f,.61f),actor.HumanIndex<0?suit:ivory);
            Box("Helmet crown stripe",body,new Vector3(0,1.047f,-.015f),new Vector3(.14f,.045f,.30f),suit);
            Box("Visor surround",body,new Vector3(0,.77f,.252f),new Vector3(.54f,.31f,.13f),suit);
            Box("Smoked face glass",body,new Vector3(0,.77f,.318f),new Vector3(.43f,.22f,.085f),dark);
            for(int side=-1;side<=1;side+=2)
            {
                Box("Pixel eye",body,new Vector3(side*.112f,.796f,.368f),new Vector3(actor.HumanIndex<0?.089f:.066f,.074f,.023f),eye);
                Sphere("Helmet ear",body,new Vector3(side*.32f,.76f,0),new Vector3(.105f,.24f,.24f),suit);
            }
            Box("Visor smile",body,new Vector3(0,.711f,.368f),new Vector3(.085f,.024f,.018f),eye);
            Transform antenna=Group("Antenna",body); antenna.localPosition=new Vector3(.18f,1.01f,-.08f);
            Cylinder("Antenna stem",antenna,new Vector3(0,.08f,0),new Vector3(.034f,.075f,.034f),dark);
            Sphere("Antenna lamp",antenna,new Vector3(0,.16f,0),Vector3.one*.085f,eye);
            if(actor.HumanIndex<0 && actor.BotKind>0)
            {
                for(int side=-1;side<=1;side+=2)
                {
                    Transform fin=Box("Rival helmet fin",body,new Vector3(side*.23f,1.05f,-.10f),new Vector3(.09f,.23f,.13f),suit);
                    fin.localRotation=Quaternion.Euler(0,0,side*-20);
                }
            }
            var visual=new ActorVisual { Root=root.gameObject, Body=body,Antenna=antenna };
            visual.LeftLeg=BuildLimb(body,"Left boot",new Vector3(-.13f,.22f,0),new Vector3(.16f,.16f,.24f),dark,false);
            visual.RightLeg=BuildLimb(body,"Right boot",new Vector3(.13f,.22f,0),new Vector3(.16f,.16f,.24f),dark,false);
            visual.LeftArm=BuildLimb(body,"Left glove",new Vector3(-.255f,.49f,0),new Vector3(.17f,.18f,.17f),suit,true);
            visual.RightArm=BuildLimb(body,"Right glove",new Vector3(.255f,.49f,0),new Vector3(.17f,.18f,.17f),suit,true);
            visual.Shield=Ring("Temporary shield ring",root,new Vector3(0,.06f,0),new Vector3(.9f,1,.9f),Mat("shield light",C("BCEDCF"),0,.2f,.65f));
            visual.Marker=Ring("Player identification",root,new Vector3(0,.07f,0),new Vector3(.66f,1,.66f),suit);
            // Two small lights encode the second human without text floating over the action.
            if(actor.HumanIndex>=0)
                for(int i=0;i<=actor.HumanIndex;i++)
                    Box("Player pip",body,new Vector3((i-actor.HumanIndex*.5f)*.075f,.54f,.171f),new Vector3(.046f,.046f,.02f),ivory);
            else visual.Marker.gameObject.SetActive(false);
            return visual;
        }

        Transform BuildLimb(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool arm)
        {
            Transform pivot=Group(name,parent); pivot.localPosition=position;
            if(arm) Sphere("Shoulder joint",pivot,Vector3.zero,Vector3.one*.11f,dark);
            Box(name,pivot,new Vector3(0,arm?-.11f:-.08f,arm?0:.025f),size,material);
            return pivot;
        }

        void RenderActors(SessionState state,float dt,bool menu)
        {
            foreach(var actor in state.Actors)
            {
                if(!actors.TryGetValue(actor.Id,out ActorVisual view)) { view=BuildActor(actor); actors.Add(actor.Id,view); }
                view.Root.SetActive(actor.Alive); if(!actor.Alive) continue;
                Vector2 p=actor.Position;
                view.Root.transform.localPosition=Cell(p.x,p.y,.025f);
                bool moving=actor.MoveProgress<.999f;
                float step=clock*(moving?actor.Speed*5.6f:2f)+actor.Id;
                float bounce=moving?Mathf.Abs(Mathf.Sin(step))*.055f:Mathf.Sin(step)*.015f;
                view.Body.localPosition=Vector3.up*bounce;
                float targetAngle=menu?154:Mathf.Atan2(actor.FacingX,actor.FacingY)*Mathf.Rad2Deg;
                view.Angle=Mathf.LerpAngle(view.Angle,targetAngle,1-Mathf.Exp(-dt*20));
                view.Body.localRotation=Quaternion.Euler(moving?6:0,view.Angle, moving?Mathf.Sin(step)*3:Mathf.Sin(step)*1.2f);
                float stride=moving?Mathf.Sin(step)*28:0;
                view.LeftLeg.localRotation=Quaternion.Euler(stride,0,0);
                view.RightLeg.localRotation=Quaternion.Euler(-stride,0,0);
                view.LeftArm.localRotation=Quaternion.Euler(-stride*.85f,0,-7);
                view.RightArm.localRotation=Quaternion.Euler(stride*.85f,0,7);
                view.Antenna.localRotation=Quaternion.Euler(Mathf.Sin(step)*5,0,Mathf.Cos(step)*7);
                view.Shield.gameObject.SetActive(actor.Shield>0);
                float shieldSize=.94f+Mathf.Sin(clock*6)*.08f;
                view.Shield.localScale=new Vector3(shieldSize,1,shieldSize);
                if(actor.WallPass)
                {
                    view.Marker.gameObject.SetActive(true);
                    view.Marker.localScale=new Vector3(.72f+Mathf.Sin(clock*3)*.07f,1,.72f+Mathf.Sin(clock*3)*.07f);
                }
            }
        }

        BombVisual BuildBomb(BombState bomb)
        {
            Transform root=Group("Bomb "+bomb.Id,effectsRoot);
            root.localPosition=Cell(bomb.X,bomb.Y,.06f);
            Cylinder("Bomb shadow",root,new Vector3(0,0,0),new Vector3(.66f,.004f,.66f),shadow,false);
            Transform body=Group("Pulsing charge",root);
            Sphere("Black powder shell",body,new Vector3(0,.29f,0),Vector3.one*.55f,dark);
            Box("Bomb collar",body,new Vector3(0,.55f,0),new Vector3(.18f,.095f,.18f),trim);
            Box("Fuse cord",body,new Vector3(.015f,.64f,0),new Vector3(.048f,.14f,.048f),woodLight).localRotation=Quaternion.Euler(0,0,-15);
            Material warm=Mat("live fuse",C("FFB350"),0,.3f,1.25f);
            Transform fuse=Sphere("Fuse ember",body,new Vector3(.04f,.72f,0),Vector3.one*.11f,warm);
            Ring("Charge equator",body,new Vector3(0,.3f,0),new Vector3(.58f,1,.58f),metal);
            Box("Charge status light",body,new Vector3(0,.32f,-.274f),new Vector3(.09f,.045f,.025f),warm);
            Transform ring=Ring("Fuse countdown",root,new Vector3(0,.016f,0),new Vector3(.85f,1,.85f),warm);
            return new BombVisual{Root=root.gameObject,Body=body,Fuse=fuse,Ring=ring};
        }

        void RenderBombs(SessionState state)
        {
            liveKeys.Clear();
            foreach(var bomb in state.Bombs)
            {
                liveKeys.Add(bomb.Id);
                if(!bombs.TryGetValue(bomb.Id,out BombVisual visual)) { visual=BuildBomb(bomb); bombs.Add(bomb.Id,visual); }
                float urgency=1-Mathf.Clamp01(bomb.Timer/2.25f);
                float pulse=Mathf.Sin(clock*(8+urgency*20));
                float size=1+urgency*.045f+pulse*(.016f+urgency*.045f);
                visual.Body.localScale=new Vector3(size,1+urgency*.02f-pulse*.03f,size);
                visual.Fuse.localScale=Vector3.one*(.10f+pulse*.035f);
                visual.Ring.localScale=new Vector3(.77f+urgency*.16f,1,.77f+urgency*.16f);
                visual.Ring.localRotation=Quaternion.Euler(0,clock*100,0);
                // A deterministic low-frequency spark, independent of gameplay RNG.
                if(particles.Count<130 && Mathf.FloorToInt(clock*15)!=Mathf.FloorToInt((clock-Time.unscaledDeltaTime)*15))
                    Burst(visual.Root.transform.localPosition+new Vector3(.04f,.72f,0),accent,1,.022f,.8f,.25f,false);
            }
            staleKeys.Clear(); foreach(var pair in bombs) if(!liveKeys.Contains(pair.Key)) staleKeys.Add(pair.Key);
            foreach(int key in staleKeys) { Destroy(bombs[key].Root); bombs.Remove(key); }
        }

        Material PickupMaterial(PickupKind kind)
        {
            switch(kind)
            {
                case PickupKind.BombUp:return Mat("pickup bomb",C("FFD173"),.1f,.5f,.22f);
                case PickupKind.SpeedUp:return Mat("pickup speed",C("86E7D2"),.1f,.5f,.22f);
                case PickupKind.FireUp:return Mat("pickup flame",C("FF9D80"),.1f,.5f,.22f);
                default:return Mat("pickup wall pass",C("BDACFA"),.1f,.5f,.22f);
            }
        }

        PickupVisual BuildPickup(PickupState pickup)
        {
            Transform root=Group(pickup.Kind.ToString(),effectsRoot); root.localPosition=Cell(pickup.X,pickup.Y,.05f);
            Material color=PickupMaterial(pickup.Kind);
            Cylinder("Pickup plinth",root,new Vector3(0,.075f,0),new Vector3(.62f,.06f,.62f),dark);
            Transform halo=Ring("Pickup beacon",root,new Vector3(0,.15f,0),new Vector3(.68f,1,.68f),color);
            Transform badge=Group("Floating pickup",root); badge.localPosition=Vector3.up*.50f;
            Box("Token rim",badge,Vector3.zero,new Vector3(.43f,.43f,.13f),color);
            Box("Token face",badge,new Vector3(0,0,-.075f),new Vector3(.34f,.34f,.035f),dark);
            Box("Token face",badge,new Vector3(0,0,.075f),new Vector3(.34f,.34f,.035f),dark);
            for(int sign=-1;sign<=1;sign+=2)
            {
                float z=sign*.103f;
                if(pickup.Kind==PickupKind.BombUp)
                {
                    Sphere("Bomb pictogram",badge,new Vector3(-.025f,-.025f,z),new Vector3(.19f,.19f,.035f),color);
                    Box("Fuse pictogram",badge,new Vector3(.03f,.079f,z),new Vector3(.045f,.07f,.028f),color);
                    Box("Plus vertical",badge,new Vector3(.103f,.095f,z),new Vector3(.025f,.085f,.028f),ivory);
                    Box("Plus horizontal",badge,new Vector3(.103f,.095f,z),new Vector3(.085f,.025f,.028f),ivory);
                }
                else if(pickup.Kind==PickupKind.SpeedUp)
                {
                    Transform a=Box("Lightning top",badge,new Vector3(-.025f,.058f,z),new Vector3(.072f,.17f,.035f),color); a.localRotation=Quaternion.Euler(0,0,-32);
                    Transform b=Box("Lightning bottom",badge,new Vector3(.025f,-.058f,z),new Vector3(.072f,.17f,.035f),color); b.localRotation=Quaternion.Euler(0,0,-32);
                }
                else if(pickup.Kind==PickupKind.FireUp)
                {
                    Transform flame=Crystal("Flame pictogram",badge,new Vector3(0,0,z),new Vector3(.20f,.30f,.04f),color);
                    Crystal("Flame heart",badge,new Vector3(0,-.03f,z+sign*.025f),new Vector3(.09f,.14f,.015f),ivory);
                }
                else
                {
                    Box("Wall pictogram",badge,new Vector3(-.083f,0,z),new Vector3(.06f,.24f,.025f),color);
                    Box("Wall pictogram",badge,new Vector3(.083f,0,z),new Vector3(.06f,.24f,.025f),color);
                    Box("Pass pictogram",badge,new Vector3(0,0,z+sign*.025f),new Vector3(.22f,.04f,.02f),ivory);
                }
            }
            return new PickupVisual{Root=root.gameObject,Badge=badge,Halo=halo};
        }

        void RenderPickups(SessionState state)
        {
            liveKeys.Clear();
            foreach(var pickup in state.Pickups)
            {
                int key=pickup.Y*width+pickup.X; liveKeys.Add(key);
                if(!pickups.TryGetValue(key,out PickupVisual visual)) { visual=BuildPickup(pickup); pickups.Add(key,visual); }
                visual.Badge.localPosition=new Vector3(0,.49f+Mathf.Sin(clock*2.7f+key)*.075f,0);
                visual.Badge.localRotation=Quaternion.Euler(0,Mathf.Sin(clock*.9f+key)*25,Mathf.Sin(clock*1.2f+key)*6);
                float scale=.63f+Mathf.Sin(clock*3+key)*.06f;
                visual.Halo.localScale=new Vector3(scale,1,scale);
            }
            staleKeys.Clear(); foreach(var pair in pickups) if(!liveKeys.Contains(pair.Key)) staleKeys.Add(pair.Key);
            foreach(int key in staleKeys) { Destroy(pickups[key].Root); pickups.Remove(key); }
        }

        void RenderFlames(SessionState state)
        {
            liveKeys.Clear();
            Material outer=Mat("blast orange",C("FF8E46"),0,.15f,.8f);
            Material core=Mat("blast core",C("FFF1BD"),0,.1f,1.5f);
            foreach(var flame in state.Flames)
            {
                int key=flame.Y*width+flame.X; liveKeys.Add(key);
                if(!flames.TryGetValue(key,out Transform visual))
                {
                    visual=Group("Flame cell",effectsRoot); visual.localPosition=Cell(flame.X,flame.Y,.1f);
                    Box("Fire footprint",visual,new Vector3(0,.035f,0),new Vector3(.88f,.09f,.88f),outer);
                    Sphere("Fire body",visual,new Vector3(0,.25f,0),new Vector3(.72f,.66f,.72f),outer);
                    Sphere("Incandescent center",visual,new Vector3(0,.25f,-.13f),new Vector3(.47f,.48f,.47f),core);
                    for(int i=0;i<3;i++)
                        Crystal("Fire tongue",visual,new Vector3((i-1)*.19f,.55f,.08f),new Vector3(.22f,.51f-i*.05f,.22f),i==1?core:outer);
                    flames.Add(key,visual);
                }
                float end=Mathf.Clamp01(flame.Timer*7);
                visual.localScale=new Vector3(.86f+Mathf.Sin(clock*38+key)*.08f,(.8f+Mathf.Sin(clock*27+key)*.14f)*end,.86f+Mathf.Cos(clock*31+key)*.06f);
            }
            staleKeys.Clear(); foreach(var pair in flames) if(!liveKeys.Contains(pair.Key)) staleKeys.Add(pair.Key);
            foreach(int key in staleKeys) { Destroy(flames[key].gameObject); flames.Remove(key); }
        }

        void Burst(Vector3 point,Material material,int count,float size,float speed,float life,bool gravity)
        {
            for(int i=0;i<count && particles.Count<240;i++)
            {
                float a=(float)decorativeRandom.NextDouble()*Mathf.PI*2;
                float s=speed*(.3f+(float)decorativeRandom.NextDouble()*.7f);
                Transform piece=Piece("Transient spark",effectsRoot,point,Vector3.one*size,material,gravity?boxMesh:sphereMesh,false);
                float duration=life*(.65f+(float)decorativeRandom.NextDouble()*.35f);
                particles.Add(new Particle{Transform=piece,Velocity=new Vector3(Mathf.Cos(a)*s,(.4f+(float)decorativeRandom.NextDouble())*s,Mathf.Sin(a)*s),Spin=new Vector3(i*74+34,i*131+80,124),Life=duration,Remaining=duration,Size=size,Gravity=gravity});
            }
        }

        void UpdateParticles(float dt)
        {
            for(int i=particles.Count-1;i>=0;i--)
            {
                Particle p=particles[i]; p.Remaining-=dt;
                if(p.Remaining<=0) { Destroy(p.Transform.gameObject); particles.RemoveAt(i); continue; }
                p.Velocity+=Vector3.down*(p.Gravity?8:1.3f)*dt;
                p.Transform.localPosition+=p.Velocity*dt;
                p.Transform.Rotate(p.Spin*dt,Space.Self);
                float fraction=p.Remaining/p.Life;
                p.Transform.localScale=Vector3.one*p.Size*Mathf.Min(1,fraction*2);
                if(p.Transform.localPosition.y<.07f && p.Gravity)
                {
                    Vector3 pos=p.Transform.localPosition; pos.y=.07f; p.Transform.localPosition=pos;
                    p.Velocity=new Vector3(p.Velocity.x*.65f,Mathf.Abs(p.Velocity.y)*.32f,p.Velocity.z*.65f);
                }
            }
        }

        Transform Group(string name,Transform parent) { var go=new GameObject(name); go.transform.SetParent(parent,false); return go.transform; }
        Transform Box(string name,Transform parent,Vector3 position,Vector3 scale,Material material) => Piece(name,parent,position,scale,material,boxMesh);
        Transform Sphere(string name,Transform parent,Vector3 position,Vector3 scale,Material material) => Piece(name,parent,position,scale,material,sphereMesh);
        Transform Cylinder(string name,Transform parent,Vector3 position,Vector3 scale,Material material,bool cast=true) => Piece(name,parent,position,scale,material,cylinderMesh,cast);
        Transform Ring(string name,Transform parent,Vector3 position,Vector3 scale,Material material) => Piece(name,parent,position,scale,material,ringMesh,false);
        Transform Crystal(string name,Transform parent,Vector3 position,Vector3 scale,Material material) => Piece(name,parent,position,scale,material,crystalMesh);
        Transform Piece(string name,Transform parent,Vector3 position,Vector3 scale,Material material,Mesh mesh,bool cast=true)
        {
            var go=new GameObject(name); Transform t=go.transform; t.SetParent(parent,false); t.localPosition=position; t.localScale=scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent<MeshRenderer>(); renderer.sharedMaterial=material;
            renderer.shadowCastingMode=cast?ShadowCastingMode.On:ShadowCastingMode.Off;
            renderer.receiveShadows=cast;
            return t;
        }

        Light NewLight(string name,LightType type,Color color,float intensity)
        {
            Transform t=Group(name,transform); var light=t.gameObject.AddComponent<Light>(); light.type=type; light.color=color; light.intensity=intensity; return light;
        }
        Material Mat(string name,Color color,float metallic,float smoothness,float emission=0)
        {
            if(materials.TryGetValue(name,out Material material)) return material;
            material=new Material(Shader.Find("Standard")); material.name=name;
            material.color=color; material.SetFloat("_Metallic",metallic); material.SetFloat("_Glossiness",smoothness);
            if(emission>0) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor",color*emission); }
            materials.Add(name,material); return material;
        }
        Material TransparentMat(string name,Color color)
        {
            if(materials.TryGetValue(name,out Material existing)) return existing;
            Material material=Mat(name,color,0,0);
            material.SetFloat("_Mode",3); material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha); material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite",0);
            material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue=3000;
            return material;
        }

        Mesh PrimitiveMesh(PrimitiveType type)
        {
            GameObject temp=GameObject.CreatePrimitive(type);
            Mesh mesh=temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp); return mesh;
        }

        void BatchStatic()
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();
            MeshFilter[] filters=staticRoot.GetComponentsInChildren<MeshFilter>();
            foreach(var filter in filters)
            {
                var renderer=filter.GetComponent<MeshRenderer>(); Material mat=renderer.sharedMaterial;
                if(!groups.TryGetValue(mat,out var entries)) { entries=new List<CombineInstance>(); groups.Add(mat,entries); }
                entries.Add(new CombineInstance{mesh=filter.sharedMesh,transform=staticRoot.worldToLocalMatrix*filter.transform.localToWorldMatrix});
            }
            foreach(var filter in filters) Destroy(filter.gameObject);
            foreach(var pair in groups)
            {
                var mesh=new Mesh{name="Batched "+pair.Key.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(pair.Value.ToArray(),true,true); mesh.RecalculateBounds(); combinedMeshes.Add(mesh);
                Piece("Architecture · "+pair.Key.name,staticRoot,Vector3.zero,Vector3.one,pair.Key,mesh);
            }
        }

        static Mesh BuildBeveledBox(float bevel)
        {
            var vertices=new List<Vector3>(); var triangles=new List<int>();
            float a=.5f,b=.5f-bevel;
            Action<Vector3[]> face=points=>AddFace(vertices,triangles,points);
            // Six inset faces.
            for(int axis=0;axis<3;axis++) for(int sign=-1;sign<=1;sign+=2)
            {
                var p=new Vector3[4];
                for(int n=0;n<4;n++) { p[n][axis]=sign*a; p[n][(axis+1)%3]=(n==0||n==3)?-b:b; p[n][(axis+2)%3]=n<2?-b:b; }
                face(p);
            }
            // Twelve bevel strips.
            for(int axis=0;axis<3;axis++) for(int s=-1;s<=1;s+=2) for(int t=-1;t<=1;t+=2)
            {
                int other=(axis+1)%3,run=(axis+2)%3;
                var p=new Vector3[4];
                p[0][axis]=s*a;p[0][other]=t*b;p[0][run]=-b;
                p[1][axis]=s*a;p[1][other]=t*b;p[1][run]=b;
                p[2][axis]=s*b;p[2][other]=t*a;p[2][run]=b;
                p[3][axis]=s*b;p[3][other]=t*a;p[3][run]=-b;
                face(p);
            }
            for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
                face(new[]{new Vector3(x*a,y*b,z*b),new Vector3(x*b,y*a,z*b),new Vector3(x*b,y*b,z*a)});
            Mesh mesh=new Mesh{name="Soft chamfer cube"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        static void AddFace(List<Vector3> vertices,List<int> triangles,Vector3[] p)
        {
            Vector3 center=Vector3.zero; foreach(Vector3 v in p) center+=v;
            bool reverse=Vector3.Dot(Vector3.Cross(p[1]-p[0],p[2]-p[0]),center)<0;
            int index=vertices.Count; vertices.AddRange(p);
            for(int i=1;i<p.Length-1;i++) { triangles.Add(index);triangles.Add(index+(reverse?i+1:i));triangles.Add(index+(reverse?i:i+1)); }
        }

        static Mesh BuildRing(float inner,float outer,int segments)
        {
            var vertices=new Vector3[segments*4]; var triangles=new int[segments*6];
            for(int i=0;i<segments;i++)
            {
                float a=i*Mathf.PI*2/segments,b=(i+1)*Mathf.PI*2/segments;
                int n=i*4;
                vertices[n]=new Vector3(Mathf.Sin(a)*inner,0,Mathf.Cos(a)*inner);
                vertices[n+1]=new Vector3(Mathf.Sin(a)*outer,0,Mathf.Cos(a)*outer);
                vertices[n+2]=new Vector3(Mathf.Sin(b)*outer,0,Mathf.Cos(b)*outer);
                vertices[n+3]=new Vector3(Mathf.Sin(b)*inner,0,Mathf.Cos(b)*inner);
                int t=i*6;triangles[t]=n;triangles[t+1]=n+1;triangles[t+2]=n+2;triangles[t+3]=n;triangles[t+4]=n+2;triangles[t+5]=n+3;
            }
            var mesh=new Mesh{name="Inlaid ring"};mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        static Mesh BuildCrystal()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<4;i++)
            {
                float a=i*Mathf.PI*.5f,b=(i+1)*Mathf.PI*.5f;
                Vector3 p=new Vector3(Mathf.Cos(a)*.5f,-.2f,Mathf.Sin(a)*.5f),q=new Vector3(Mathf.Cos(b)*.5f,-.2f,Mathf.Sin(b)*.5f);
                AddFace(vertices,triangles,new[]{p,q,Vector3.up*.6f});
                AddFace(vertices,triangles,new[]{q,p,Vector3.down*.5f});
            }
            var mesh=new Mesh{name="Faceted crystal"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }

        void OnDestroy()
        {
            foreach(Material material in materials.Values) if(material) Destroy(material);
            foreach(Mesh mesh in combinedMeshes) if(mesh) Destroy(mesh);
            foreach(Mesh mesh in primitiveMeshes) if(mesh) Destroy(mesh);
        }
    }
}
