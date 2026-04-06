using System.Collections.Generic;
using UnityEngine;

namespace MMONetworking.Client
{
public sealed class UnityMmoSpellEffectSystem : MonoBehaviour
{
    [SerializeField] private Color fireballColor = new Color(1f, 0.42f, 0.12f);
    [SerializeField] private Color explosionColor = new Color(1f, 0.76f, 0.24f);
    [SerializeField] private float projectileSpeed = 30f;
    [SerializeField] private float projectileScale = 0.55f;
    [SerializeField] private float impactScale = 1.35f;

    private readonly List<ProjectileVisual> _projectiles = new List<ProjectileVisual>();
    private readonly List<ExplosionVisual> _explosions = new List<ExplosionVisual>();
    private Transform _container;

    private void Awake()
    {
        var containerObject = new GameObject("Spell Effects");
        containerObject.transform.SetParent(transform, false);
        _container = containerObject.transform;
    }

    private void Update()
    {
        var now = Time.time;
        for (var index = _projectiles.Count - 1; index >= 0; index--)
        {
            var projectile = _projectiles[index];
            var t = Mathf.Clamp01((now - projectile.StartTime) / projectile.Duration);
            projectile.GameObject.transform.position = Vector3.Lerp(projectile.StartPosition, projectile.EndPosition, t);
            projectile.GameObject.transform.localScale = Vector3.one * Mathf.Lerp(projectileScale, projectileScale * 0.55f, t);
            if (t < 1f)
            {
                continue;
            }

            SpawnExplosion(projectile.EndPosition);
            Destroy(projectile.GameObject);
            _projectiles.RemoveAt(index);
        }

        for (var index = _explosions.Count - 1; index >= 0; index--)
        {
            var explosion = _explosions[index];
            var t = Mathf.Clamp01((now - explosion.StartTime) / explosion.Duration);
            explosion.GameObject.transform.localScale = Vector3.one * Mathf.Lerp(0.35f, impactScale, t);
            if (explosion.Renderer != null)
            {
                var color = Color.Lerp(explosionColor, new Color(explosionColor.r, explosionColor.g, explosionColor.b, 0.08f), t);
                UnityMmoMaterialFactory.Apply(explosion.Renderer, color);
            }

            if (t < 1f)
            {
                continue;
            }

            Destroy(explosion.GameObject);
            _explosions.RemoveAt(index);
        }
    }

    public void LaunchFireball(Vector3 startPosition, Vector3 targetPosition)
    {
        var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = "Fireball";
        projectile.transform.SetParent(_container, false);
        projectile.transform.position = startPosition;
        projectile.transform.localScale = Vector3.one * projectileScale;
        var projectileCollider = projectile.GetComponent<Collider>();
        if (projectileCollider != null)
        {
            Destroy(projectileCollider);
        }

        var renderer = projectile.GetComponent<Renderer>();
        if (renderer != null)
        {
            UnityMmoMaterialFactory.Apply(renderer, fireballColor);
        }

        var distance = Vector3.Distance(startPosition, targetPosition);
        var duration = Mathf.Clamp(distance / Mathf.Max(6f, projectileSpeed), 0.18f, 0.9f);

        _projectiles.Add(new ProjectileVisual
        {
            GameObject = projectile,
            StartPosition = startPosition,
            EndPosition = targetPosition,
            StartTime = Time.time,
            Duration = duration
        });
    }

    private void SpawnExplosion(Vector3 position)
    {
        var explosion = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        explosion.name = "Fireball Impact";
        explosion.transform.SetParent(_container, false);
        explosion.transform.position = position;
        explosion.transform.localScale = Vector3.one * 0.35f;
        var explosionCollider = explosion.GetComponent<Collider>();
        if (explosionCollider != null)
        {
            Destroy(explosionCollider);
        }

        var renderer = explosion.GetComponent<Renderer>();
        if (renderer != null)
        {
            UnityMmoMaterialFactory.Apply(renderer, explosionColor);
        }

        _explosions.Add(new ExplosionVisual
        {
            GameObject = explosion,
            Renderer = renderer,
            StartTime = Time.time,
            Duration = 0.22f
        });
    }

    private sealed class ProjectileVisual
    {
        public GameObject GameObject;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public float StartTime;
        public float Duration;
    }

    private sealed class ExplosionVisual
    {
        public GameObject GameObject;
        public Renderer Renderer;
        public float StartTime;
        public float Duration;
    }
}
}
