using System;
using System.Collections.Generic;

namespace MovementPrototype {
    public enum FocusRecoveryResult { Waiting, Recovered, Failed }

    // This policy never sends movement or changes focus itself. The caller must
    // release held movement keys before Begin, then supply a normal focus request.
    // A successful request is not proof: only a later stable ready observation is.
    public sealed class FocusRecovery {
        public const double InitialDelay = 0.35;
        public const double RequestInterval = 0.5;
        public const double StableDuration = 0.15;
        public const double Timeout = 2.5;
        public const int MaximumAttempts = 3;

        double began, previous, nextRequest, readySince;
        bool observingReady;
        FocusRecoveryResult result = FocusRecoveryResult.Failed;

        public bool Active { get; private set; }
        public int Attempts { get; private set; }
        public string Status { get; private set; }
        public string LastFailure { get; private set; }

        public FocusRecovery() {
            Status = "Focus recovery is inactive";
            LastFailure = "Focus recovery has not started";
        }

        static bool Finite(double time) {
            return !Double.IsNaN(time) && !Double.IsInfinity(time);
        }

        public void Begin(double now) {
            if (!Finite(now)) throw new ArgumentOutOfRangeException("now", "Use a finite monotonic time.");
            began = previous = now;
            nextRequest = now + InitialDelay;
            readySince = 0;
            observingReady = false;
            Attempts = 0;
            Active = true;
            result = FocusRecoveryResult.Waiting;
            LastFailure = null;
            Status = "Paused — waiting to return focus";
        }

        public void Cancel() {
            Active = false;
            observingReady = false;
            result = FocusRecoveryResult.Failed;
            LastFailure = "Focus recovery cancelled";
            Status = LastFailure;
        }

        FocusRecoveryResult Fail(string reason) {
            Active = false;
            observingReady = false;
            result = FocusRecoveryResult.Failed;
            LastFailure = reason;
            Status = reason;
            return result;
        }

        public FocusRecoveryResult Step(double now, bool available, bool ready, bool busy, Action requestFocus) {
            // Completed and cancelled operations are terminal until Begin is called.
            if (!Active) return result;
            if (!Finite(now) || now < previous) return Fail("Stopped — focus recovery clock was invalid");
            previous = now;
            if (!available) return Fail("Stopped — target closed, changed, or was minimized");
            if (now - began >= Timeout) return Fail("Stopped — Windows did not restore stable focus in time");

            if (ready && !busy) {
                if (!observingReady) { readySince = now; observingReady = true; }
                if (now - readySince >= StableDuration) {
                    Active = false;
                    result = FocusRecoveryResult.Recovered;
                    Status = "Focus confirmed — ready to resume";
                    return result;
                }
                Status = "Paused — confirming target focus";
                return FocusRecoveryResult.Waiting;
            }

            // A fresh click, drag, modifier, or another focus loss invalidates any
            // stability interval already observed. Busy input is never interrupted.
            observingReady = false;
            if (busy) {
                Status = "Paused — release mouse buttons and modifier keys";
                return FocusRecoveryResult.Waiting;
            }

            if (now >= nextRequest && Attempts < MaximumAttempts) {
                Attempts++;
                nextRequest = now + RequestInterval;
                if (requestFocus == null) return Fail("Stopped — no focus request was available");
                try { requestFocus(); }
                catch (Exception error) { return Fail("Stopped — focus request failed: " + error.Message); }
                Status = "Paused — requesting focus (" + Attempts + "/" + MaximumAttempts + ")";
            } else {
                Status = Attempts == MaximumAttempts ? "Paused — waiting for Windows to confirm focus" : "Paused — waiting to return focus";
            }
            return FocusRecoveryResult.Waiting;
        }
    }

    public static class FocusRecoveryTests {
        static void Check(bool condition, string message) {
            if (!condition) throw new Exception("Focus recovery: " + message);
        }

        public static List<string> Run() {
            var passed = new List<string>();
            int calls = 0;
            Action request = delegate { calls++; };
            var recovery = new FocusRecovery();

            recovery.Begin(0);
            Check(recovery.Step(0, true, false, false, request) == FocusRecoveryResult.Waiting && calls == 0, "no immediate focus request");
            recovery.Step(0.349, true, false, false, request);
            Check(calls == 0, "initial delay");
            recovery.Step(0.35, true, false, false, request);
            recovery.Step(0.849, true, false, false, request);
            Check(calls == 1, "minimum interval");
            recovery.Step(0.85, true, false, false, request);
            recovery.Step(1.35, true, false, false, request);
            recovery.Step(2.49, true, false, false, request);
            Check(calls == 3 && recovery.Attempts == 3 && recovery.Active, "maximum three requests while awaiting confirmation");
            Check(recovery.Step(2.5, true, false, false, request) == FocusRecoveryResult.Failed && !recovery.Active, "bounded timeout");
            Check(recovery.Step(3, true, true, false, request) == FocusRecoveryResult.Failed && calls == 3, "failure remains terminal");
            passed.Add("PASS: focus requests honor initial delay, spacing, three-attempt cap, and terminal timeout");

            calls = 0;
            recovery.Begin(10);
            Check(recovery.Step(10.1, true, true, false, request) == FocusRecoveryResult.Waiting, "ready must first stabilize");
            Check(recovery.Step(10.24, true, true, false, request) == FocusRecoveryResult.Waiting, "short ready interval cannot resume");
            Check(recovery.Step(10.251, true, true, false, request) == FocusRecoveryResult.Recovered && !recovery.Active && calls == 0, "stable ready can recover without a request");
            Check(recovery.Step(100, false, false, true, request) == FocusRecoveryResult.Recovered && calls == 0, "success remains terminal");
            passed.Add("PASS: resume requires stable confirmed focus; request success alone never resumes");

            calls = 0;
            recovery.Begin(0);
            recovery.Step(0.4, true, false, true, request);
            recovery.Step(1.2, true, true, true, request);
            Check(recovery.Step(2.5, true, false, true, request) == FocusRecoveryResult.Failed && calls == 0, "busy input waits but does not extend timeout");
            passed.Add("PASS: mouse/modifier activity prevents focus requests and cannot extend the recovery deadline");

            calls = 0;
            recovery.Begin(0);
            Check(recovery.Step(0.1, false, false, false, request) == FocusRecoveryResult.Failed && calls == 0, "unavailable target fails immediately");
            recovery.Begin(10);
            recovery.Step(10.36, true, false, false, request);
            Check(recovery.Step(10.37, false, false, false, request) == FocusRecoveryResult.Failed && calls == 1, "target availability is checked again after a request");
            passed.Add("PASS: closed, replaced, or minimized targets end focus recovery without further requests");

            calls = 0;
            recovery.Begin(0);
            recovery.Step(0, true, true, false, request);
            recovery.Step(0.1, true, false, false, request);
            recovery.Step(0.2, true, true, false, request);
            Check(recovery.Step(0.31, true, true, false, request) == FocusRecoveryResult.Waiting, "focus loss resets stability interval");
            Check(recovery.Step(0.351, true, true, false, request) == FocusRecoveryResult.Recovered && calls == 0, "fresh stable interval recovers");
            recovery.Begin(1);
            recovery.Step(1, true, true, false, request);
            recovery.Step(1.1, true, true, true, request);
            Check(recovery.Step(1.2, true, true, false, request) == FocusRecoveryResult.Waiting, "busy input also resets stability");
            Check(recovery.Step(1.351, true, true, false, request) == FocusRecoveryResult.Recovered, "fresh stability after busy input");
            passed.Add("PASS: repeated focus loss and busy input reset the stable-focus observation");

            calls = 0;
            recovery.Begin(0);
            recovery.Step(0.35, true, false, false, request);
            recovery.Cancel();
            Check(recovery.Step(100, true, false, false, request) == FocusRecoveryResult.Failed && !recovery.Active && calls == 1, "cancel stops all retries");
            recovery.Begin(200);
            Check(recovery.Attempts == 0 && recovery.LastFailure == null, "explicit new begin resets prior cancellation");
            recovery.Step(200.36, true, false, false, request);
            Check(calls == 2 && recovery.Attempts == 1, "new begin can request focus independently");
            passed.Add("PASS: stop/emergency cancellation prevents late focus changes; explicit restart resets the policy");

            calls = 0;
            recovery.Begin(0);
            recovery.Step(2.4, true, false, false, request);
            Check(calls == 1, "delayed tick does not replay missed requests");
            recovery.Step(2.5, true, false, false, request);
            Check(calls == 1 && !recovery.Active, "late recovery still has original deadline");
            recovery.Begin(4);
            Check(recovery.Step(3.9, true, false, false, request) == FocusRecoveryResult.Failed, "backwards clock stops");
            recovery.Begin(5);
            Check(recovery.Step(Double.NaN, true, false, false, request) == FocusRecoveryResult.Failed, "invalid clock stops");
            recovery.Begin(6);
            Check(recovery.Step(6.4, true, false, false, delegate { throw new InvalidOperationException("denied"); }) == FocusRecoveryResult.Failed, "request exception stops");
            passed.Add("PASS: delayed ticks do not burst requests; invalid clocks and focus-request errors stop recovery");

            return passed;
        }
    }
}
