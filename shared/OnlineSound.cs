// Sons da sala, gerados por código (sem arquivos de áudio). Por enquanto só o "ding" de quem entra na sala.
using UnityEngine;

public static class OnlineSound
{
    private static AudioSource source;
    private static AudioClip clip;

    /// <summary>Duas notas curtas e agudas, para avisar que alguém entrou na sala.</summary>
    public static void Ding()
    {
        try
        {
            if (source == null)
            {
                var go = new GameObject("OnlineSound");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                clip = MakeClip();
            }
            source.PlayOneShot(clip, 0.7f);
        }
        catch (System.Exception)
        {
            // sem áudio disponível: o aviso sonoro é só um extra
        }
    }

    private static AudioClip MakeClip()
    {
        const int rate = 44100;
        const float noteLength = 0.16f;
        int n = (int)(rate * noteLength * 2.2f);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            bool second = t >= noteLength;
            float local = second ? t - noteLength : t;
            float freq = second ? 1318.5f : 880f; // lá e mi, uma quinta acima
            float envelope = Mathf.Min(1f, local / 0.008f) * Mathf.Exp(-7f * local);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.55f;
        }
        var c = AudioClip.Create("online-ding", n, 1, rate, false);
        c.SetData(data, 0);
        return c;
    }
}
