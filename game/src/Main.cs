using Godot;
using Wtw.Sim.Core;
using Wtw.Sim.Runtime;
using Wtw.Sim.World;

namespace Wtw.Game;

/// <summary>
/// 시작점. 시뮬(SimHost, 별도 스레드)을 띄우고, 매 프레임 스냅샷을 받아 지도와 막대를 그린다. 입력 → 시뮬 명령.
/// 명령줄(사용자 인자, -- 뒤): --seed=N --dots=N --speed=N --screenshot=경로 --shot-after=초 --selftest
/// </summary>
public partial class Main : Node
{
    private SimHost _host = null!;
    private MapView _map = null!;
    private Hud _hud = null!;
    private WorldConfig _config = new();
    private readonly AutoPilot _auto = new();

    private static string SavePath => ProjectSettings.GlobalizePath("user://saves/quicksave.wtw");

    public override void _Ready()
    {
        var theme = UiTheme.Build();
        _auto.Parse(OS.GetCmdlineUserArgs());

        ulong seed = _auto.Seed ?? NewSeed();
        _config = new WorldConfig { DotCount = _auto.Dots ?? 500 };
        _host = new SimHost(GameWorld.Create(seed, _config));
        _host.SetSpeed(_auto.Speed ?? 1);

        _map = new MapView();
        AddChild(_map);

        var ui = new CanvasLayer();
        AddChild(ui);
        _hud = new Hud { Theme = theme };
        ui.AddChild(_hud);
        _hud.SaveRequested += () => _host.Save(SavePath);
        _hud.LoadRequested += () => _host.Load(SavePath);
        _hud.NewRequested += () => _host.NewWorld(NewSeed(), _config);
        _hud.SpeedRequested += s => { if (s == 0) _host.SetPaused(true); else _host.SetSpeed(s); };

        _auto.Start(this);
    }

    private static ulong NewSeed() => StableHash.Of((ulong)DateTime.UtcNow.Ticks) % 1_000_000_000UL;

    public override void _Process(double delta)
    {
        var snap = _host.Frame(delta);
        _map.Render(snap, _host.RenderTick);
        _hud.UpdateInfo(snap, _host, delta);
        while (_host.TryGetNotice(out var n))
        {
            _hud.ShowNotice(n);
            _auto.OnNotice(n);
        }
        _auto.Tick(delta, snap);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            switch (k.Keycode)
            {
                case Key.Space: _host.TogglePause(); break;
                case Key.Key1 or Key.Kp1: _host.SetSpeed(1); break;
                case Key.Key2 or Key.Kp2: _host.SetSpeed(2); break;
                case Key.Key3 or Key.Kp3: _host.SetSpeed(3); break;
                case Key.Key4 or Key.Kp4: _host.SetSpeed(4); break;
                case Key.Key5 or Key.Kp5: _host.SetSpeed(5); break;
                case Key.F5: _host.Save(SavePath); break;
                case Key.F9: _host.Load(SavePath); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
        }
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } mb && _map.TryToMap(mb.Position, out long x, out long y))
        {
            _host.Intervene(Intervention.Kinds.Gather, x, y);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() => _host?.Dispose();

    // AutoPilot 이 쓰는 문
    internal SimHost Host => _host;
    internal string QuickSavePath => SavePath;
}
