// Bridges the browser's built-in speech recognition (the Web Speech API —
// window.SpeechRecognition / webkitSpeechRecognition) into VoiceLetterListener
// for WebGL builds. No keys, no server of ours: Chrome/Edge/Android use
// Google's recognizer, Safari (macOS/iOS) uses Siri's. Firefox has none and
// reports unsupported, which the Call screen answers with letter buttons.
//
// Protocol: Unity polls OtherwiseSpeech_Poll() every frame while listening and
// receives one queued message per call:
//   "R:<transcript>\t<alt2>\t<alt3>..."  a final result with its alternatives
//   "S:listening"                        the microphone is live
//   "E:<code>"                           a recognition error (not-allowed, ...)
// Polling keeps the bridge free of any GameObject-name coupling (no
// SendMessage), so VoiceLetterListener can live on any object.

mergeInto(LibraryManager.library, {

  OtherwiseSpeech_IsSupported: function () {
    return (typeof window !== "undefined" &&
            (window.SpeechRecognition || window.webkitSpeechRecognition)) ? 1 : 0;
  },

  OtherwiseSpeech_Start: function (languagePtr) {
    var language = UTF8ToString(languagePtr);

    if (!window.__otherwiseSpeech) {
      window.__otherwiseSpeech = {
        queue: [], wanted: false, running: false, rec: null, gestureHooked: false
      };
    }
    var state = window.__otherwiseSpeech;
    state.wanted = true;
    state.queue.length = 0;

    var Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) { state.queue.push("E:unsupported"); return; }

    // Reads everything through `state`, so the one copy hooked to the gesture
    // listener below keeps working for every later recognizer instance.
    var safeStart = function () {
      if (!state.wanted || state.running || !state.rec) return;
      try { state.rec.start(); } catch (ignored) { /* already starting */ }
    };

    if (state.rec) {
      try { state.rec.abort(); } catch (ignored) {}
      state.rec = null;
      state.running = false;
    }

    var rec = new Recognition();
    rec.lang = language || "en-US";
    rec.continuous = true;        // keep listening across utterances
    rec.interimResults = false;   // finals only: one clean report per utterance
    rec.maxAlternatives = 4;      // letter names are short; alternatives rescue them

    rec.onstart = function () {
      state.running = true;
      state.queue.push("S:listening");
    };
    rec.onresult = function (event) {
      for (var i = event.resultIndex; i < event.results.length; i++) {
        var result = event.results[i];
        if (!result.isFinal) continue;
        var alternatives = [];
        for (var j = 0; j < result.length; j++) alternatives.push(result[j].transcript);
        state.queue.push("R:" + alternatives.join("\t"));
      }
    };
    rec.onerror = function (event) {
      state.queue.push("E:" + (event.error || "unknown"));
    };
    // Browsers end recognition on every silence (iOS ends it after EVERY
    // utterance, continuous or not): restart for as long as Unity wants ears.
    rec.onend = function () {
      state.running = false;
      if (state.wanted) setTimeout(safeStart, 250);
    };

    state.rec = rec;
    safeStart();

    // Some browsers only wake the microphone from inside a real user gesture,
    // and Unity's input runs outside the DOM event stack. This hook is a
    // no-op while recognition runs, and revives it on the next tap otherwise.
    if (!state.gestureHooked) {
      state.gestureHooked = true;
      document.addEventListener("pointerdown", safeStart, true);
    }
  },

  OtherwiseSpeech_Stop: function () {
    var state = window.__otherwiseSpeech;
    if (!state) return;
    state.wanted = false;
    state.queue.length = 0;
    if (state.rec) {
      try { state.rec.abort(); } catch (ignored) {}
      state.rec = null;
    }
    state.running = false;
  },

  OtherwiseSpeech_Poll: function () {
    var state = window.__otherwiseSpeech;
    if (!state || state.queue.length === 0) return 0;
    var message = state.queue.shift();
    var size = lengthBytesUTF8(message) + 1;
    var buffer = _malloc(size);
    stringToUTF8(message, buffer, size);
    return buffer;
  }
});
