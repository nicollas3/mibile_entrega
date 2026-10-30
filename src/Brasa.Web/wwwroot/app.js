window.brasaAlert = () => {
  const Audio = window.AudioContext || window.webkitAudioContext;
  if (!Audio) return;
  const context = new Audio();
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  oscillator.connect(gain); gain.connect(context.destination);
  oscillator.frequency.value = 660; gain.gain.value = 0.08;
  oscillator.start(); oscillator.stop(context.currentTime + 0.18);
  oscillator.onended = () => context.close();
};
