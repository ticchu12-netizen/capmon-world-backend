using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[ExecuteInEditMode]
public class UIParticleSystem : MaskableGraphic
{
    public new ParticleSystem particleSystem;
    private List<UIVertex> vertices = new List<UIVertex>();
    private List<int> indices = new List<int>();
    private ParticleSystem.Particle[] particles;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (particleSystem == null) return;

        int numParticles = particleSystem.GetParticles(particles);
        vertices.Clear();
        indices.Clear();

        for (int i = 0; i < numParticles; i++)
        {
            ParticleSystem.Particle p = particles[i];
            Vector3 position = p.position;
            float size = p.GetCurrentSize(particleSystem) / 2;
            Color color = p.GetCurrentColor(particleSystem);

            // Create quad for each particle
            UIVertex v = UIVertex.simpleVert;
            v.color = color;

            v.position = position + new Vector3(-size, -size, 0);
            vertices.Add(v);
            v.position = position + new Vector3(size, -size, 0);
            vertices.Add(v);
            v.position = position + new Vector3(size, size, 0);
            vertices.Add(v);
            v.position = position + new Vector3(-size, size, 0);
            vertices.Add(v);

            int start = i * 4;
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);
        }

        vh.AddUIVertexStream(vertices, indices);
    }

    void Update()
    {
        if (particleSystem != null)
        {
            particles = new ParticleSystem.Particle[particleSystem.main.maxParticles];
            SetAllDirty();
        }
    }
}