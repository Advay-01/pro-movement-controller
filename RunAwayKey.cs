using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MovementPrototype {
    public sealed class RunAwayKey {
        readonly Action<Keys,bool> send;
        public const Keys Key = Keys.RShiftKey;
        public bool Held { get; private set; }
        public double ReleaseAt { get; private set; }
        public RunAwayKey(Action<Keys,bool> sender) { send=sender; }
        public void Press(double now) {
            if(Held) throw new InvalidOperationException("Run key is already held.");
            Held=true; ReleaseAt=now+0.12;
            // Keep ownership even on an uncertain send, so cleanup tries key-up.
            send(Key,false);
        }
        public void Release() {
            if(!Held) return;
            send(Key,true);
            Held=false;
        }
    }
    public static class RunAwayKeyTests {
        public static List<string> Run() {
            var events=new List<string>(); bool fail=false;
            var key=new RunAwayKey(delegate(Keys k,bool up) { events.Add(k+":"+up); if(fail) throw new Exception("input failed"); });
            key.Press(10);
            if(!key.Held || key.ReleaseAt<10.119 || events[0]!="RShiftKey:False") throw new Exception("Run must press and own right Shift");
            key.Release(); key.Release();
            if(key.Held || events.Count!=2 || events[1]!="RShiftKey:True") throw new Exception("Run must release exactly once");
            fail=true; try { key.Press(20); } catch(Exception) { }
            if(!key.Held) throw new Exception("Failed down must retain release ownership");
            try { key.Release(); } catch(Exception) { }
            if(!key.Held) throw new Exception("Failed up must retain release ownership");
            fail=false; key.Release();
            if(key.Held) throw new Exception("Retry must release Shift");
            return new List<string> { "PASS: run-away uses right Shift, releases once, and retries cleanup after failed input" };
        }
    }
}
