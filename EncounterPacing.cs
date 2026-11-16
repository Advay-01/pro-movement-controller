using System;
using System.Collections.Generic;

namespace MovementPrototype {
    // Only the movement decision is smoothed. Opponent identity is never cached for fleeing.
    public sealed class EncounterPacing {
        public const double WorldScanSeconds=.5, PausedScanSeconds=.25;
        public const double WorldGraceSeconds=1.5, ConfirmationWindowSeconds=1.5;
        double? lastWorld, confirmationBegan;
        int confirmations;
        bool confirming=true;
        public double LastConfirmationSeconds { get; private set; }
        public void Reset() { lastWorld=null; confirmationBegan=null; confirmations=0; confirming=true; }
        public bool PauseFor(bool battle, bool world, double now) {
            if(battle) { Reset(); return true; }
            if(world) {
                lastWorld=now;
                if(!confirming) return false;
                if(!confirmationBegan.HasValue || now-confirmationBegan.Value>ConfirmationWindowSeconds) {
                    confirmationBegan=now; confirmations=1;
                } else confirmations++;
                if(confirmations<2) return true;
                LastConfirmationSeconds=now-confirmationBegan.Value;
                confirming=false;
                return false;
            }
            if(!confirming && lastWorld.HasValue && now-lastWorld.Value<=WorldGraceSeconds) return false;
            if(!confirming) { confirming=true; confirmationBegan=null; confirmations=0; }
            return true;
        }
        public static bool RunDue(int confirmedReads, int attempts, bool held, double elapsed) {
            return confirmedReads>=2 && !held && (attempts==0 || elapsed>=2);
        }
    }
    public static class EncounterPacingTests {
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        public static List<string> Run() {
            var p=new EncounterPacing();
            Check(p.PauseFor(false,false,0),"Unknown startup must remain paused");
            Check(p.PauseFor(false,true,1),"First world frame cannot resume");
            Check(!p.PauseFor(false,true,1.25),"Second clear frame should resume promptly");
            Check(!p.PauseFor(false,false,1.5) && !p.PauseFor(false,false,2.75),"Brief missed readings must not interrupt confirmed movement");
            Check(p.PauseFor(false,false,2.751),"Expired world evidence must pause");
            Check(p.PauseFor(false,true,3) && !p.PauseFor(false,true,3.25),"Fresh confirmation resumes after expiry");
            Check(p.PauseFor(true,true,3.3),"Battle evidence always pauses immediately");
            Check(p.PauseFor(false,false,3.4),"World grace cannot carry through a battle");
            Check(p.PauseFor(false,true,4) && p.PauseFor(false,false,4.1) && !p.PauseFor(false,true,4.25),"An intervening unclear frame does not erase recent positive confirmation");
            Check(Math.Abs(p.LastConfirmationSeconds-.25)<.001,"Resume latency should follow two fast frames");
            p.Reset();
            Check(p.PauseFor(false,true,10) && p.PauseFor(false,true,12) && !p.PauseFor(false,true,12.25),"Old positive frames cannot combine across a long gap");
            p.Reset(); Check(p.PauseFor(false,false,13),"Focus/reset cannot reuse world evidence");
            Check(!EncounterPacing.RunDue(1,0,false,5),"One species reading can never flee");
            Check(EncounterPacing.RunDue(2,0,false,0),"Two confirmed non-target reads must allow an immediate first action");
            Check(!EncounterPacing.RunDue(2,1,false,1.99) && EncounterPacing.RunDue(2,1,false,2),"Retry backoff remains two seconds");
            Check(!EncounterPacing.RunDue(2,0,true,5),"Held run key cannot be pressed again");
            return new List<string> {
                "PASS: brief OCR misses preserve world movement; battle, expired evidence and focus reset pause immediately",
                "PASS: two recent world readings resume in 250ms; stale readings cannot combine",
                "PASS: first escape has no fixed delay after two species readings; release checks and retry backoff remain active"
            };
        }
    }
}
