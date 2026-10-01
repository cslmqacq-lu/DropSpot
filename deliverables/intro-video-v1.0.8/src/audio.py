import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io import wavfile
SR=48000; DUR=60.0; N=int(SR*DUR)
rng=np.random.default_rng(7)
mus=np.zeros((N,2)); sfx=np.zeros((N,2))
def nf(n): return 440*2**((n-69)/12)
def put(buf,t,sig,gain=1.0,pan=0.0):
    i=int(t*SR); 
    if i>=N: return
    sig=sig[:N-i]
    l=np.cos((pan+1)*np.pi/4); r=np.sin((pan+1)*np.pi/4)
    buf[i:i+len(sig),0]+=sig*gain*l*1.414; buf[i:i+len(sig),1]+=sig*gain*r*1.414
def lp(x,f,o=2): return sosfilt(butter(o,f,'low',fs=SR,output='sos'),x)
def hp(x,f,o=2): return sosfilt(butter(o,f,'high',fs=SR,output='sos'),x)
def bp(x,a,b,o=2): return sosfilt(butter(o,[a,b],'band',fs=SR,output='sos'),x)
def env(n,a,r):
    e=np.ones(n); A=int(a*SR); R=int(r*SR)
    if A: e[:A]=np.linspace(0,1,A)
    if R: e[-R:]*=np.linspace(1,0,R)
    return e
# ---------------- 音乐 ----------------
BEAT=0.6; BAR=4*BEAT
chords=[[53,57,60,64],[52,55,59,62],[50,53,57,60],[48,52,55,59]]  # Fmaj7 Em7 Dm7 Cmaj7
def pad(notes,dur):
    n=int(dur*SR); t=np.arange(n)/SR; s=np.zeros(n)
    for m in notes:
        f=nf(m+12)
        for d in (-0.15,0.15):
            s+=np.sin(2*np.pi*f*(1+d/100)*t)+0.3*np.sin(4*np.pi*f*t)+0.12*np.sin(6*np.pi*f*t)
    s=lp(s,1800)*env(n,0.25,0.5)
    return s/len(notes)/3
def kick():
    n=int(.32*SR); t=np.arange(n)/SR
    f=45+80*np.exp(-t*30); ph=2*np.pi*np.cumsum(f)/SR
    return np.sin(ph)*np.exp(-t*11)
def snare():
    n=int(.22*SR); t=np.arange(n)/SR
    return bp(rng.standard_normal(n),900,5000)*np.exp(-t*22)*0.6+np.sin(2*np.pi*190*t)*np.exp(-t*30)*0.3
def hat():
    n=int(.06*SR); t=np.arange(n)/SR
    return hp(rng.standard_normal(n),7000)*np.exp(-t*70)
def pluck(m,dur=.5):
    n=int(dur*SR); t=np.arange(n)/SR; f=nf(m)
    s=(np.sin(2*np.pi*f*t)+.4*np.sin(4*np.pi*f*t)+.15*np.sin(6*np.pi*f*t))*np.exp(-t*7)
    return lp(s,3500)
def bass(m,dur):
    n=int(dur*SR); t=np.arange(n)/SR; f=nf(m-12)
    return lp(np.sin(2*np.pi*f*t)+.3*np.sin(4*np.pi*f*t),400)*np.exp(-t*2.2)*env(n,.01,.08)
BEAT_START=6.0
nbars=int(DUR/BAR)+1
mel=[[72,76,79,76],[71,74,76,74],[69,72,74,72],[67,71,72,74]]
for b in range(nbars):
    t0=b*BAR; ch=chords[b%4]
    put(mus,t0,pad(ch,BAR+0.5),0.22)
    if t0+BAR<=BEAT_START: continue
    put(mus,t0,bass(ch[0],BEAT*1.5),0.35); put(mus,t0+BEAT*2.5,bass(ch[0],BEAT*1.2),0.28)
    for k in range(4):
        tb=t0+k*BEAT
        if tb<BEAT_START or tb>58.6: continue
        if k in (0,2): put(mus,tb,kick(),0.55)
        if k in (1,3): put(mus,tb,snare(),0.18)
        put(mus,tb+BEAT/2,hat(),0.06,0.3); put(mus,tb,hat(),0.04,-0.3)
    if t0>=8.0 and t0<56:
        for k,m in enumerate(mel[b%4]):
            put(mus,t0+k*BEAT+ (0.3 if k%2 else 0),pluck(m),0.07,0.25 if k%2 else -0.25)
# 结尾长音
put(mus,57.6,pad([48,52,55,59,62],2.4),0.25)
fade=np.ones(N); fs=int(1.4*SR); fade[-fs:]=np.linspace(1,0,fs); mus*=fade[:,None]
# 开头 6 秒更轻一点
g=np.ones(N); g[:int(6*SR)]=0.7; mus*=g[:,None]
# ---------------- 音效 ----------------
def whoosh(d=.45):
    n=int(d*SR); t=np.arange(n)/SR; x=rng.standard_normal(n)
    out=np.zeros(n); seg=n//12
    for i in range(12):
        f=600+3000*np.sin(np.pi*i/11)
        a=i*seg; b=min(n,a+seg+400)
        out[a:b]+=bp(x[a:b],f*.6,f*1.4)
    return out*np.sin(np.pi*t/d)**2*0.9
def pop(f0=520,f1=980):
    n=int(.09*SR); t=np.arange(n)/SR; f=np.linspace(f0,f1,n)
    return np.sin(2*np.pi*np.cumsum(f)/SR)*np.exp(-t*40)
def ding(f=1318,d=.9):
    n=int(d*SR); t=np.arange(n)/SR
    return (np.sin(2*np.pi*f*t)+.5*np.sin(2*np.pi*f*2.01*t)+.25*np.sin(2*np.pi*f*3*t))*np.exp(-t*5)*0.6
def click():
    n=int(.03*SR); t=np.arange(n)/SR
    return hp(rng.standard_normal(n),2500)*np.exp(-t*200)+np.sin(2*np.pi*2200*t)*np.exp(-t*150)*.5
def thud():
    n=int(.15*SR); t=np.arange(n)/SR
    return np.sin(2*np.pi*(140*np.exp(-t*8))*t)*np.exp(-t*25)
def bonk():
    n=int(.18*SR); t=np.arange(n)/SR
    return lp(np.sign(np.sin(2*np.pi*180*t)),1200)*np.exp(-t*20)*.5
for t in [1.5,13.25,27.15,31.25,33.95,40.2,48.3]: put(sfx,t,whoosh(),0.35)
for i,t in enumerate([3.15,3.45,3.75,4.05,8.3,8.5,8.62,8.74,8.86,8.98,9.1,19.95,26.15,42.6,42.75,42.9,43.05,49.4,56.1,57.4,32.65]):
    put(sfx,t,pop(),0.32,(-.3 if i%2 else .3))
for t,f in [(6.45,1318),(13.95,1318),(16.6,1568),(29.0,1318),(32.7,1568),(34.9,2093),(52.75,1318),(54.2,1568),(56.4,1046)]: put(sfx,t,ding(f),0.28)
for t in [1.35,15.4,15.65,37.5,37.7,37.9,39.6,39.8,40.0,47.25]: put(sfx,t,click(),0.45)
for i in range(12): put(sfx,4.7+i*.08+.4,thud(),0.25,(i%3-1)*.4)
for t in [43.95,44.79,45.63,46.47]: put(sfx,t,bonk(),0.35,-.2)
put(sfx,48.5,pop(900,500),0.4)
mix=mus*0.9+sfx
mix=np.tanh(mix*1.1)*0.85
wavfile.write('bgm_sfx.wav',SR,(mix*32767).astype(np.int16))
wavfile.write('music_only.wav',SR,(np.tanh(mus*0.9*1.1)*0.85*32767).astype(np.int16))
print('peak',np.abs(mix).max())
