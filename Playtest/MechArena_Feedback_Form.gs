/**
 * Mech Arena — Playtest Feedback Form generator (minimal: 8 questions, ~2 min)
 *
 * HOW TO USE (one time, ~2 minutes):
 *   1. Go to https://script.google.com and click "New project"
 *   2. Delete the placeholder code, paste this whole file, press Save
 *   3. Press "Run" (function createPlaytestForm). Approve the permission prompt
 *      (it only asks for Forms/Drive access on YOUR account).
 *   4. Open the "Execution log" — it prints two links:
 *        - Editor link  (for you, to tweak questions)
 *        - Live link    (give THIS one to playtesters, and paste it into
 *                        MechArena_TestPlan.docx + the report)
 *
 * Safe to re-run: it wipes and rebuilds the questions each time,
 * so running twice never duplicates anything.
 */
function createPlaytestForm() {
  // Fills the empty form already sitting in your Drive (created 2026-07-25).
  // If you'd rather make a brand-new form, swap this line for:
  //   var form = FormApp.create('Mech Arena — Playtest Feedback');
  var form = FormApp.openById('1_lxyhVK_8m-UTsMdXqX1od-EI5mTwLKn-7P-sd3x_tQ');
  form.setTitle('Mech Arena — Playtest Feedback');
  form.setDescription(
    'Thanks for playtesting! 8 quick questions, about 2 minutes. ' +
    'Answer honestly - negative feedback is the useful kind.');
  form.setCollectEmail(false);
  form.setProgressBar(false);

  // Start clean so re-running the script never duplicates questions.
  form.getItems().forEach(function (item) { form.deleteItem(item); });

  form.addMultipleChoiceItem()
    .setTitle('1. How much experience do you have with fast action games?')
    .setChoiceValues([
      'Almost none',
      'Casual (I play sometimes)',
      'Experienced (I play action games regularly)',
      'I have played Gundam EXVS / Starward-style arena fighters'])
    .setRequired(true);

  form.addScaleItem()
    .setTitle('2. After the tutorial, how prepared did you feel for the real fight?')
    .setBounds(1, 5).setLabels('Totally lost', 'Fully prepared')
    .setRequired(true);

  form.addScaleItem()
    .setTitle('3. How did the mech feel to control? (moving, dashing, rising)')
    .setBounds(1, 5).setLabels('Very clunky', 'Very responsive')
    .setRequired(true);

  form.addScaleItem()
    .setTitle('4. How did the combat feel? (melee combos + shooting)')
    .setBounds(1, 5).setLabels('Unresponsive / random', 'Satisfying and reliable')
    .setRequired(true);

  form.addCheckboxItem()
    .setTitle('5. Which of these did you understand and actually use? (tick all that apply)')
    .setChoiceValues([
      'Charge shot (hold right-click, release)',
      'Shield (Q) to block attacks',
      'Punishing the enemy after a blocked melee (parry stun)',
      'Rainbow step (boost-step to cancel a combo into a new one)',
      'The yellow knockdown bar (fill it to floor the enemy)',
      'None of these'])
    .setRequired(true);

  form.addScaleItem()
    .setTitle('6. Camera & readability: how well could you follow the action?')
    .setBounds(1, 5).setLabels('Constantly lost / blocked', 'Always clear')
    .setRequired(true);

  form.addScaleItem()
    .setTitle('7. How difficult was the enemy?')
    .setBounds(1, 5).setLabels('Way too easy', 'Way too hard')
    .setRequired(true);

  form.addParagraphTextItem()
    .setTitle('8. What was the MOST FRUSTRATING moment (bugs count!), and the one thing you would change?')
    .setRequired(true);

  Logger.log('=== FORM READY ===');
  Logger.log('Editor link (for you):    ' + form.getEditUrl());
  Logger.log('Live link (for testers):  ' + form.getPublishedUrl());
}
