using UnityEngine;

namespace OtherwiseLabs.CreatureGame
{
    /// <summary>
    /// One roaming letter-creature. Added at runtime to a spawned model, so the
    /// art prefabs need no setup at all.
    ///
    /// The brain is a small state machine:
    ///  - Wandering: amble between habitat-appropriate points, with a soft pull
    ///    toward any word-paper trap bearing this creature's letter.
    ///  - Lured: the player called this creature's name — walk to them, even out
    ///    of the water, then resume wandering.
    ///  - Stuck: standing on a word paper. No walking away; only tracing the
    ///    letter frees (captures) it.
    ///  - Captured: a short victory moment, then gone to the journal.
    ///
    /// Movement is transform-driven with a ground-snapping raycast; the model's
    /// Animator (RPG Monster DUO controllers have no parameters) is driven by
    /// state name, guarded so a prefab with different states just keeps playing
    /// its default.
    /// </summary>
    public class LetterCreature : MonoBehaviour
    {
        public CreatureDefinition Definition { get; private set; }
        public State CurrentState { get; private set; }
        public WordTrapPaper StuckOn { get; private set; }

        public enum State { Wandering, Lured, Stuck, Captured }

        CreatureGameController _game;
        Animator _animator;
        Vector3 _target;
        float _waitUntil;
        float _luredUntil;
        float _dizzyRefreshAt;
        float _nextEscapeAttempt;
        float _nextBaitScanAt;
        float _nextShyReactAt;
        float _fleeUntil;
        int _holdCount;
        string _playingState;

        const float ArriveDistance = 0.6f;
        const float TurnSpeed = 360f;

        public char Letter => Definition.Letter;

        /// <summary>Called by the controller right after instantiating the model.</summary>
        public void Setup(CreatureDefinition definition, CreatureGameController game)
        {
            Definition = definition;
            _game = game;
            _animator = GetComponentInChildren<Animator>();

            // A trigger capsule + kinematic body: the trap's trigger needs a
            // rigidbody in the pair to fire, and kinematic keeps physics from
            // ever shoving the player or the creature around.
            var body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.height = 1.6f;
            capsule.radius = 0.6f;
            capsule.center = new Vector3(0f, 0.8f, 0f);

            PickWanderTarget();
            SnapToGround(instant: true);
        }

        void Update()
        {
            if (_game == null) return;

            switch (CurrentState)
            {
                case State.Wandering:
                    ReactToNearbyPlayer();
                    if (Time.time < _waitUntil) { Play("IdleNormal"); return; }
                    if (MoveTowards(_target, Time.time < _fleeUntil ? Definition.walkSpeed * 1.7f : Definition.walkSpeed))
                    {
                        _waitUntil = Time.time + Random.Range(1.5f, 4f);
                        PickWanderTarget();
                    }
                    break;

                case State.Lured:
                    // The call draws the creature INTO the area; a word paper it
                    // could stick to takes over from there. This is the intended
                    // hunt: shout to bring them close, let the paper do the rest.
                    if (Time.time >= _nextBaitScanAt)
                    {
                        _nextBaitScanAt = Time.time + 0.7f;
                        WordTrapPaper bait = WordTrapPaper.FindBaitFor(Letter, transform.position, _game.trapAttractRadius);
                        if (bait != null && !_game.PlayerIsNear(bait.transform.position, _game.playerShyRadius))
                            _target = bait.transform.position;
                    }

                    if (Time.time > _luredUntil || MoveTowards(_target, Definition.walkSpeed * 1.6f))
                    {
                        CurrentState = State.Wandering;
                        _waitUntil = Time.time + 1f;
                        PickWanderTarget();
                    }
                    break;

                case State.Stuck:
                    // A loosely-held creature (one occurrence of its letter in
                    // the word) periodically tries to wriggle free. Two or more
                    // means glued: no escape attempts at all.
                    if (_holdCount <= 1 && Time.time >= _nextEscapeAttempt)
                    {
                        if (Random.value < 0.5f) { Escape(); return; }
                        _nextEscapeAttempt = Time.time + Random.Range(5f, 9f);
                    }

                    // The Dizzy clip does not loop on these controllers, so
                    // nudge it back now and then to keep the creature wobbling.
                    if (Time.time >= _dizzyRefreshAt)
                    {
                        _playingState = null;
                        Play("Dizzy");
                        _dizzyRefreshAt = Time.time + 2.6f;
                    }
                    break;

                case State.Captured:
                    // Shrink away during the victory moment; the controller
                    // destroys us when the celebration ends.
                    transform.localScale = Vector3.MoveTowards(
                        transform.localScale, Vector3.zero, Time.deltaTime * Definition.modelScale);
                    break;
            }
        }

        /// <summary>The player called this creature's name nearby: come running.</summary>
        public void Lure(Vector3 target, float duration)
        {
            if (CurrentState == State.Stuck || CurrentState == State.Captured) return;
            CurrentState = State.Lured;
            _target = target;
            _luredUntil = Time.time + duration;
        }

        /// <summary>Stepped on a word paper. Called by the paper's trigger.</summary>
        public void BecomeStuck(WordTrapPaper paper)
        {
            if (CurrentState == State.Stuck || CurrentState == State.Captured) return;
            CurrentState = State.Stuck;
            StuckOn = paper;
            _dizzyRefreshAt = 0f;

            // The word decides the grip: one occurrence of our letter can be
            // wriggled out of after a while, two or more never lets go.
            _holdCount = paper.HoldCount(Letter);
            _nextEscapeAttempt = Time.time + Random.Range(6f, 11f);

            // Stand on the paper itself so the tableau reads clearly.
            Vector3 center = paper.transform.position;
            center.y = transform.position.y;
            transform.position = center;
        }

        /// <summary>Wriggled off a weak paper: dash clear and go back to roaming.</summary>
        void Escape()
        {
            WordTrapPaper paper = StuckOn;
            if (paper != null) paper.NotifyEscaped();
            StuckOn = null;
            CurrentState = State.Wandering;
            _waitUntil = 0f;
            _playingState = null;
            Play("GetHit");

            Vector3 away = paper != null ? (transform.position - paper.transform.position) : Random.insideUnitSphere;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = new Vector3(1f, 0f, 0f);
            _target = transform.position + away.normalized * 8f;
        }

        /// <summary>Trap destroyed without a capture — free to roam again.</summary>
        public void Release()
        {
            if (CurrentState != State.Stuck) return;
            CurrentState = State.Wandering;
            StuckOn = null;
            _waitUntil = Time.time + 0.5f;
            PickWanderTarget();
        }

        /// <summary>The letter was traced correctly. Celebrate; the controller finishes up.</summary>
        public void BeginCapture()
        {
            CurrentState = State.Captured;
            StuckOn = null;
            Play("Victory");
        }

        /// <summary>A failed trace: the stuck creature teases the player before wobbling on.</summary>
        public void TauntWhileStuck()
        {
            if (CurrentState != State.Stuck) return;
            _playingState = null;
            Play("Taunt");
            _dizzyRefreshAt = Time.time + 2.2f;
        }

        /// <summary>
        /// Wild creatures visibly shy away from a close player: startle, then
        /// scurry off at a trot. This is what makes standing next to your own
        /// trap counterproductive — and stepping back part of the hunt. A
        /// called (lured) creature trusts the voice and skips the fear.
        /// </summary>
        void ReactToNearbyPlayer()
        {
            if (Time.time < _nextShyReactAt) return;
            if (!_game.PlayerIsNear(transform.position, _game.playerShyRadius)) return;

            _nextShyReactAt = Time.time + 1.2f;
            _fleeUntil = Time.time + 2f;
            _waitUntil = 0f; // no standing around while spooked

            Vector3 away = transform.position - _game.Player.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = new Vector3(1f, 0f, 0f);
            _target = transform.position + away.normalized * (_game.playerShyRadius + 4f);
        }

        // ------------------------------------------------------------------

        /// <summary>Moves toward a point (XZ), snapping to the ground. True on arrival.</summary>
        bool MoveTowards(Vector3 target, float speed)
        {
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            if (flat.magnitude <= ArriveDistance)
            {
                Play("IdleNormal");
                return true;
            }

            Play("WalkFWD");
            Vector3 direction = flat.normalized;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(direction), TurnSpeed * Time.deltaTime);
            transform.position += direction * (speed * Time.deltaTime);
            SnapToGround(instant: false);
            return false;
        }

        void SnapToGround(bool instant)
        {
            if (!_game.SampleGround(transform.position.x, transform.position.z, out float groundY)) return;

            // Water dwellers ride just under the surface while over submerged
            // ground — reads as swimming without any real buoyancy.
            float y = groundY;
            if (Definition.habitat == CreatureHabitat.Water && groundY < _game.WaterSurfaceY - 0.3f)
                y = _game.WaterSurfaceY - 0.35f;

            Vector3 position = transform.position;
            position.y = instant ? y : Mathf.Lerp(position.y, y, Time.deltaTime * 8f);
            transform.position = position;
        }

        void PickWanderTarget()
        {
            // A word paper containing our letter is interesting — and the MORE
            // of our letter it has, the harder it pulls. But creatures are shy:
            // a player standing over the paper keeps them away, so hunters
            // learn to lay the trap and step back.
            WordTrapPaper bait = WordTrapPaper.FindBaitFor(Letter, transform.position, _game.trapAttractRadius);
            if (bait != null && !_game.PlayerIsNear(bait.transform.position, _game.playerShyRadius))
            {
                float pull = Mathf.Min(0.9f, 0.35f + 0.25f * bait.HoldCount(Letter));
                if (Random.value < pull)
                {
                    _target = bait.transform.position;
                    return;
                }
            }

            _target = _game.PickWanderPoint(Definition, transform.position);
        }

        /// <summary>CrossFades by state name, only when the state exists and changed.</summary>
        void Play(string stateName)
        {
            if (_animator == null || _playingState == stateName) return;
            int hash = Animator.StringToHash(stateName);
            if (!_animator.HasState(0, hash)) return;
            _animator.CrossFade(hash, 0.15f);
            _playingState = stateName;
        }
    }
}
