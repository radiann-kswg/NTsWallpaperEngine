using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.UI;

public class CharacterAssetsDB : MonoBehaviour
{
    [Serializable]
    class CADB
    {
        public int number;
        public string name;
        public string name_1;
        public Sprite image;
    }

    [SerializeField]
    List<CADB> cADBs;

    CADB nowCADB;
    int nowIndex = -1;
    int numAnim;
    float alphaAnim = 0f;
    const int NUM_ANIM_DURATION = 300;

    [SerializeField]
    Color textColor = new Color(50f / 255f, 50f / 255f, 50f / 255f);

    [SerializeField]
    Image characterImage;

    [SerializeField]
    Text numText, nameText, name1Text, clockText;

    DateTime nowDateTime;
    

    // Start is called before the first frame update
    void Start()
    {
        SetNowDateTime();
        StartCoroutine(AnimateCADB(true));
    }

    // Update is called once per frame
    void Update()
    {
        if(nowDateTime.Minute != DateTime.Now.Minute)
        {
            SetNowDateTime();
            StartCoroutine(AnimateCADB(false));
        }
    }

    CADB ReturnRandomCADB()
    {
        nowIndex = Mathf.FloorToInt(UnityEngine.Random.value * cADBs.Count);
        return cADBs[nowIndex];
    }

    CADB ReturnNextCADB()
    {
        nowIndex = (nowIndex + 1) % cADBs.Count;
        return cADBs[nowIndex];
    }

    void SetNowDateTime()
    {
        nowDateTime = DateTime.Now;
        if (clockText)
            clockText.text = nowDateTime.Hour.ToString("D2") + " " + nowDateTime.Minute.ToString("D2");
    }

    void ChangeCA(bool isRandom)
    {
        nowCADB = isRandom ? ReturnRandomCADB() : ReturnNextCADB();
    }

    IEnumerator FadeOutCADB()
    {
        if (nowIndex < 0) yield break;
        while (alphaAnim > 0f)
        {
            alphaAnim -= Time.deltaTime;
            numAnim = nowCADB.number + Mathf.CeilToInt(NUM_ANIM_DURATION * (1.0f - alphaAnim));

            SetColorAlphaAnim();
            numText.text = (numAnim % 1000).ToString("D3");
            yield return null;
        }
        alphaAnim = 0f;
        SetColorAlphaAnim();
        numText.text = ((nowCADB.number + NUM_ANIM_DURATION) % 1000).ToString("D3");
    }

    IEnumerator AnimateCADB(bool isRandom)
    {
        yield return StartCoroutine(FadeOutCADB());
        ChangeCA(isRandom);
        characterImage.sprite = nowCADB.image;
        nameText.text = nowCADB.name;
        name1Text.text = nowCADB.name_1;
        while (alphaAnim < 1f)
        {
            alphaAnim += Time.deltaTime;
            numAnim = nowCADB.number - Mathf.CeilToInt(NUM_ANIM_DURATION * (1f - alphaAnim));

            SetColorAlphaAnim();
            numText.text = ((numAnim + 1000) % 1000).ToString("D3");
            yield return null;
        }
        alphaAnim = 1f;
        SetColorAlphaAnim();
        numText.text = nowCADB.number.ToString("D3");
    }

    void SetColorAlphaAnim()
    {
        characterImage.color = new Color(1f, 1f, 1f, alphaAnim);
        numText.color = nameText.color = name1Text.color = new Color(textColor.r, textColor.g, textColor.b, alphaAnim);
    }
}
