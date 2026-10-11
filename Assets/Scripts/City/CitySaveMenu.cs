using System;
using System.Collections.Generic;
using System.IO;
using CityGame.CellRoads;
using CityGame.Roads;
using CityGame.Sectors;
using UnityEngine;

namespace CityGame.Buildings
{
    /// <summary>Локальное автосохранение города и стартовое меню.</summary>
    public sealed class CitySaveMenu : MonoBehaviour
    {
        [Serializable] sealed class BuildingData { public int x, y, category, kind; }
        [Serializable] sealed class RoadData { public int x, y, mask, type; }
        [Serializable] sealed class SectorData { public int q, r; }
        [Serializable] sealed class SaveData
        {
            public int seed;
            public float money;
            public List<BuildingData> buildings = new List<BuildingData>();
            public List<RoadData> roads = new List<RoadData>();
            public List<SectorData> sectors = new List<SectorData>();
        }

        CitySystem _city;
        bool _menu = true;
        float _nextSave;
        GUIStyle _title, _sub, _button, _continue, _small, _mark;
        Texture2D _artwork, _goldButton, _darkButton;
        static string SavePath => Path.Combine(Application.persistentDataPath, "city-save.json");
        bool HasSave => File.Exists(SavePath);

        public void Bind(CitySystem city)
        {
            _city = city;
            _city.SetMenuMode(true);
        }

        void Update()
        {
            if (!_menu && Time.unscaledTime >= _nextSave)
            {
                Save();
                _nextSave = Time.unscaledTime + 2f;
            }
        }

        void OnApplicationPause(bool paused) { if (paused && !_menu) Save(); }
        void OnApplicationQuit() { if (!_menu) Save(); }

        void Save()
        {
            if (_city == null) return;
            var data = new SaveData { seed = _city.Biomes.Map.Seed, money = _city.Economy.Money };
            foreach (BuildingCell b in _city.Model.Cells)
                data.buildings.Add(new BuildingData { x = b.Pos.x, y = b.Pos.y, category = (int)b.Category, kind = (int)b.Kind });
            foreach (var road in _city.RoadMap.Cells)
                data.roads.Add(new RoadData { x = road.Key.x, y = road.Key.y, mask = road.Value, type = (int)_city.RoadMap.TypeAt(road.Key) });
            foreach (HexCoord h in _city.Sectors.Open) data.sectors.Add(new SectorData { q = h.Q, r = h.R });
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(data)); }
            catch (Exception e) { Debug.LogWarning("Не удалось сохранить город: " + e.Message); }
        }

        bool Load()
        {
            try
            {
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (data == null) return false;
                _city.Model.Clear();
                _city.RoadMap.Clear();
                _city.Sectors.RestoreOpen(ToHexes(data.sectors));
                _city.Biomes.Map.Seed = data.seed;
                if (data.buildings != null)
                    foreach (BuildingData b in data.buildings)
                        if (b != null)
                            _city.Model.Place(new Vector2Int(b.x, b.y), (BuildingCategory)b.category, (ServiceKind)b.kind);
                if (data.roads != null)
                    foreach (RoadData r in data.roads)
                        if (r != null)
                            _city.RoadMap.RestoreCell(new Vector2Int(r.x, r.y), r.mask, (JunctionType)r.type);
                _city.RoadMap.FinishRestore();
                _city.Economy.Money = data.money;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Сохранение города повреждено: " + e.Message);
                return false;
            }
        }

        static IEnumerable<HexCoord> ToHexes(List<SectorData> values)
        {
            if (values != null)
                foreach (SectorData p in values)
                    if (p != null) yield return new HexCoord(p.q, p.r);
        }

        void StartGame(bool load)
        {
            if (load && !Load()) return;
            if (!load)
            {
                _city.BeginNewCity();
                Save(); // новая игра сразу заменяет прежнее сохранение
            }
            _city.SetMenuMode(false);
            _menu = false;
            _nextSave = Time.unscaledTime + 2f;
        }

        void EnsureStyles()
        {
            if (_artwork != null) return;
            _artwork = BuildArtwork();
            _goldButton = SolidTexture(new Color32(0xD8,0x98,0x43,0xFF));
            _darkButton = SolidTexture(new Color32(0x24,0x32,0x40,0xFF));
            _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Clamp(Screen.width / 15, 28, 44), fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color32(0xF2, 0xF0, 0xE2, 0xFF);
            _sub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 13, wordWrap = true };
            _sub.normal.textColor = new Color32(0xC4, 0xCB, 0xCA, 0xFF);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(22, 18, 0, 0) };
            _button.normal.textColor = new Color32(0x2E, 0x32, 0x32, 0xFF);
            _continue = new GUIStyle(_button);
            _continue.normal.textColor = new Color32(0xEB, 0xEF, 0xE8, 0xFF);
            _small = new GUIStyle(_sub) { fontSize = 10, alignment = TextAnchor.MiddleLeft };
            _mark = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 23, fontStyle = FontStyle.Bold };
            _mark.normal.textColor = new Color32(0xF2,0xF0,0xE2,0xFF);
            ApplyButtonBackground(_button, _goldButton);
            ApplyButtonBackground(_continue, _darkButton);
        }

        static Texture2D SolidTexture(Color color)
        {
            var tex = new Texture2D(1,1,TextureFormat.RGBA32,false) { hideFlags = HideFlags.DontSave };
            tex.SetPixel(0,0,color); tex.Apply(false,false); return tex;
        }
        static void ApplyButtonBackground(GUIStyle style, Texture2D texture)
        {
            style.normal.background = texture;
            style.hover.background = texture;
            style.active.background = texture;
            style.focused.background = texture;
            style.border = new RectOffset(0,0,0,0);
        }

        static Texture2D BuildArtwork()
        {
            const int w = 420, h = 900;
            var pixels = new Color[w * h];
            Color skyTop = new Color32(0x13, 0x1D, 0x2C, 0xFF);
            Color horizon = new Color32(0x83, 0x50, 0x55, 0xFF);
            Color sea = new Color32(0x13, 0x2B, 0x38, 0xFF);
            for (int y = 0; y < h; y++)
            {
                float t = Mathf.Clamp01(y / (float)h);
                Color c = t > .53f ? Color.Lerp(horizon, skyTop, (t - .53f) / .47f) : Color.Lerp(sea, horizon, t / .53f);
                for (int x = 0; x < w; x++) pixels[y * w + x] = c;
            }
            // Тёплое солнце и дальние хребты.
            Circle(pixels, w, h, 320, 570, 59, new Color32(0xF4, 0xB9, 0x61, 0xFF));
            Poly(pixels, w, h, new Vector2Int[] { new Vector2Int(0,510),new Vector2Int(65,580),new Vector2Int(115,550),new Vector2Int(190,620),new Vector2Int(265,550),new Vector2Int(340,600),new Vector2Int(420,525),new Vector2Int(420,390),new Vector2Int(0,390) }, new Color32(0x35,0x3B,0x4B,0xFF));
            Poly(pixels, w, h, new Vector2Int[] { new Vector2Int(0,470),new Vector2Int(80,535),new Vector2Int(150,495),new Vector2Int(235,555),new Vector2Int(330,475),new Vector2Int(420,520),new Vector2Int(420,350),new Vector2Int(0,350) }, new Color32(0x25,0x37,0x3D,0xFF));
            // Остров: ровная геометрия и цвета игровых гексов.
            Poly(pixels, w, h, new Vector2Int[] { new Vector2Int(12,355),new Vector2Int(58,442),new Vector2Int(145,488),new Vector2Int(250,493),new Vector2Int(350,455),new Vector2Int(410,382),new Vector2Int(385,270),new Vector2Int(315,210),new Vector2Int(205,188),new Vector2Int(92,222),new Vector2Int(30,285) }, new Color32(0x4A,0x68,0x56,0xFF));
            Poly(pixels, w, h, new Vector2Int[] { new Vector2Int(25,365),new Vector2Int(72,435),new Vector2Int(155,470),new Vector2Int(252,475),new Vector2Int(342,440),new Vector2Int(392,375),new Vector2Int(370,286),new Vector2Int(306,232),new Vector2Int(205,212),new Vector2Int(105,242),new Vector2Int(45,294) }, new Color32(0x8E,0xB6,0x6F,0xFF));
            // Сдержанные грани гексов.
            for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++)
            {
                int cx = 85 + col * 72 + (row % 2) * 34, cy = 280 + row * 43;
                if (cx < 52 || cx > 365 || cy > 438) continue;
                Line(pixels,w,h,cx-15,cy,cx,cy-9,new Color(0.48f,0.62f,0.43f,.38f));
                Line(pixels,w,h,cx,cy-9,cx+15,cy,new Color(0.48f,0.62f,0.43f,.38f));
            }
            // Тонкие дороги и маленькие кварталы — отсылка к игровому полю.
            Line(pixels,w,h,88,350,330,350,new Color32(0x3B,0x46,0x51,0xFF),13);
            Line(pixels,w,h,130,303,292,303,new Color32(0x3B,0x46,0x51,0xFF),13);
            Line(pixels,w,h,130,303,130,350,new Color32(0x3B,0x46,0x51,0xFF),13);
            Line(pixels,w,h,292,303,292,350,new Color32(0x3B,0x46,0x51,0xFF),13);
            RectFill(pixels,w,h,94,364,25,21,new Color32(0xE9,0xA7,0xA8,0xFF));
            RectFill(pixels,w,h,292,364,31,20,new Color32(0xE9,0xCE,0x8A,0xFF));
            RectFill(pixels,w,h,176,310,24,20,new Color32(0x8D,0xAC,0xD6,0xFF));
            for (int i=0;i<12;i++)
            {
                int x=50+(i*67)%315, y=250+(i*43)%205;
                Circle(pixels,w,h,x,y,7,new Color32(0x4E,0x7D,0x58,0xFF));
                Circle(pixels,w,h,x-2,y+3,4,new Color32(0x8E,0xB6,0x6F,0xFF));
            }
            var tex = new Texture2D(w,h,TextureFormat.RGBA32,false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels(pixels); tex.Apply(false,false); return tex;
        }

        static void RectFill(Color[] p,int w,int h,int x,int y,int rw,int rh,Color c)
        {
            for(int yy=Mathf.Max(0,y);yy<Mathf.Min(h,y+rh);yy++) for(int xx=Mathf.Max(0,x);xx<Mathf.Min(w,x+rw);xx++) p[yy*w+xx]=c;
        }
        static void Circle(Color[] p,int w,int h,int cx,int cy,int r,Color c)
        {
            for(int y=Mathf.Max(0,cy-r);y<Mathf.Min(h,cy+r);y++) for(int x=Mathf.Max(0,cx-r);x<Mathf.Min(w,cx+r);x++) if((x-cx)*(x-cx)+(y-cy)*(y-cy)<=r*r)p[y*w+x]=c;
        }
        static void Poly(Color[] p,int w,int h,Vector2Int[] pts,Color c)
        {
            int minY=h,maxY=0,minX=w,maxX=0; foreach(var v in pts){minY=Mathf.Min(minY,v.y);maxY=Mathf.Max(maxY,v.y);minX=Mathf.Min(minX,v.x);maxX=Mathf.Max(maxX,v.x);}
            for(int y=Mathf.Max(0,minY);y<Mathf.Min(h,maxY);y++) for(int x=Mathf.Max(0,minX);x<Mathf.Min(w,maxX);x++)
            { bool inside=false; for(int i=0,j=pts.Length-1;i<pts.Length;j=i++){var a=pts[i];var b=pts[j];if(((a.y>y)!=(b.y>y))&&(x<(b.x-a.x)*(y-a.y)/(float)(b.y-a.y)+a.x))inside=!inside;} if(inside)p[y*w+x]=c; }
        }
        static void Line(Color[] p,int w,int h,int x0,int y0,int x1,int y1,Color c,int width=1)
        {
            int dx=Mathf.Abs(x1-x0),sx=x0<x1?1:-1,dy=-Mathf.Abs(y1-y0),sy=y0<y1?1:-1,err=dx+dy;
            while(true){Circle(p,w,h,x0,y0,Mathf.Max(1,width)/2,c);if(x0==x1&&y0==y1)break;int e=2*err;if(e>=dy){err+=dy;x0+=sx;}if(e<=dx){err+=dx;y0+=sy;}}
        }

        void OnGUI()
        {
            if (!_menu || _city == null) return;
            // Другой IMGUI-компонент мог оставить tint; меню всегда рисуем в исходных цветах.
            Color previousGuiColor = GUI.color;
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            EnsureStyles();
            float s = Mathf.Max(.5f, Mathf.Min(Screen.width / 420f, Screen.height / 850f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float w = Screen.width / s, h = Screen.height / s;
            GUI.DrawTexture(new Rect(0,0,w,h), _artwork, ScaleMode.StretchToFill, false);
            float left = 34f;
            GUI.Box(new Rect(left, h*.10f, 42, 42), "", GUI.skin.box);
            GUI.Label(new Rect(left, h*.10f-1, 42, 42), "H", _mark);
            GUI.Label(new Rect(left+56, h*.10f-4, w-left*2-56, 50), "HEXACITY", _title);
            GUI.Label(new Rect(left+2, h*.10f+50, w-left*2, 28), "СТРОЙ ГОРОД СВОЕЙ МЕЧТЫ", _sub);

            float bw = Mathf.Min(w-68, 340), bh=54, x=(w-bw)*.5f;
            float y = h*.72f;
            if(GUI.Button(new Rect(x,y,bw,bh), "НОВЫЙ ГОРОД    ›", _button)) StartGame(false);
            y += bh+12;
            GUI.enabled=HasSave;
            if(GUI.Button(new Rect(x,y,bw,bh), "ПРОДОЛЖИТЬ    ›", _continue)) StartGame(true);
            GUI.enabled=true;
            GUI.Label(new Rect(x+2,y+bh+15,bw,24), HasSave ? "МИР СОХРАНЁН НА ЭТОМ УСТРОЙСТВЕ" : "СОЗДАЙТЕ ПЕРВЫЙ ГОРОД", _small);
            GUI.color = previousGuiColor;
            GUI.backgroundColor = previousBackgroundColor;
        }

        void OnDestroy()
        {
            if (_artwork != null) Destroy(_artwork);
            if (_goldButton != null) Destroy(_goldButton);
            if (_darkButton != null) Destroy(_darkButton);
        }
    }
}
