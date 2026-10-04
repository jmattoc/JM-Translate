"""Mide qué tan bien la comparación por significado elige la respuesta correcta del banco.
Las frases de prueba están escritas con otras palabras que las del banco, para no medir solo coincidencias literales.
"""
import time

from matcher import Matcher, default_path

BANK = {
    "Preséntate": ["Tell me about yourself.", "Can you introduce yourself?", "Walk me through your background.",
                   "Tell me a little about yourself and your experience."],
    "Fortalezas": ["What are your main strengths?", "What would you say is your greatest strength?",
                   "What are you best at?", "What do you bring to the team?"],
    ".NET y arquitectura limpia": ["Tell me about your .NET experience.", "How much experience do you have with .NET?",
                                    "What is clean architecture and why do you use it?",
                                    "How do you structure a .NET application?"],
    "Nube y pipelines": ["Tell me about your cloud experience.", "Have you worked with AWS or Azure?",
                         "How do you set up CI/CD pipelines?", "Do you have experience designing cloud architectures?"],
    "DevOps": ["Do you have DevOps experience?", "What is your experience with DevOps practices?",
               "How do you automate deployments?"],
    "SQL Server": ["Tell me about your database experience.", "How strong are you with SQL Server?",
                   "Which databases have you worked with?", "Do you have experience with MySQL or PostgreSQL?"],
    "APIs REST y JWT": ["How do you design a REST API?", "How do you secure an API?",
                        "Do you have experience with JWT authentication?", "Tell me about your experience building APIs."],
    "Front end": ["Do you have front-end experience?", "Have you worked with React or Angular?",
                  "What front-end frameworks do you know?"],
    "Móvil": ["Do you have mobile development experience?", "Have you worked with React Native?"],
    "Power BI y ETL": ["Do you have experience with Power BI?", "Have you worked with ETL processes?",
                       "How do you work with data and reporting?"],
    "Java": ["Do you know Java?", "Have you worked with Spring Boot?", "What other languages do you know besides .NET?"],
    "Calidad": ["How do you ensure code quality?", "Do you write unit tests?", "What is your testing strategy?"],
    "Consulta lenta": ["How would you optimize a slow SQL query?", "A query is running slowly, what do you do?",
                       "How do you troubleshoot database performance?"],
    "Desacuerdos": ["How do you handle disagreements with teammates?", "Tell me about a time you disagreed with someone.",
                    "How do you resolve conflicts in a team?"],
    "Por qué este puesto": ["Why are you interested in this position?", "Why do you want to work here?",
                            "What attracted you to this role?"],
    "Por qué cambiar": ["Why are you looking for a new opportunity?", "Why are you leaving your current job?",
                        "Why do you want to change jobs?"],
    "Debilidad": ["What is your biggest weakness?", "What are your weaknesses?", "What would you like to improve?"],
    "Inglés": ["How is your English?", "How would you rate your English level?",
               "Are you comfortable communicating in English?"],
    "Zona horaria": ["Are you comfortable working in our time zone?", "Can you overlap with our working hours?",
                     "Do you have experience working remotely across time zones?"],
    "Salario": ["What are your salary expectations?", "How much are you looking to earn?",
                "What is your expected compensation?"],
    "Preguntas": ["Do you have any questions for us?", "Is there anything you would like to ask us?", "Any questions for me?"],
    "Años": ["How many years of experience do you have?", "How long have you been working as a developer?",
             "How much experience do you have?"],
    "Proyecto": ["Tell me about a project you're proud of.", "What is the most interesting project you've worked on?",
                 "Have you worked on OCR or document processing?"],
    "Problema difícil": ["Tell me about a challenging problem you solved.",
                         "Describe a difficult technical problem you faced."],
    "Disponibilidad": ["When could you start?", "What is your availability?", "How soon can you join?",
                       "What is your notice period?"],
}

POSITIVE = [
    ("Could you give me a quick introduction about yourself?", "Preséntate"),
    ("Let's start with you. Who are you and what do you do?", "Preséntate"),
    ("Walk me through your resume.", "Preséntate"),
    ("What do you consider your top strengths?", "Fortalezas"),
    ("What are you really good at?", "Fortalezas"),
    ("How long have you been working with .NET?", ".NET y arquitectura limpia"),
    ("Explain how you apply clean architecture in your projects.", ".NET y arquitectura limpia"),
    ("Which cloud providers have you worked with?", "Nube y pipelines"),
    ("Tell me how you build CI/CD pipelines.", "Nube y pipelines"),
    ("Are you familiar with DevOps?", "DevOps"),
    ("How do you automate your release process?", "DevOps"),
    ("How comfortable are you with SQL Server?", "SQL Server"),
    ("What databases do you know?", "SQL Server"),
    ("How would you secure a REST endpoint?", "APIs REST y JWT"),
    ("Have you built web APIs before?", "APIs REST y JWT"),
    ("Are you comfortable with front-end work?", "Front end"),
    ("Do you know Angular?", "Front end"),
    ("Have you built mobile apps?", "Móvil"),
    ("Have you used Power BI?", "Power BI y ETL"),
    ("Do you also work with Java?", "Java"),
    ("How do you test your code?", "Calidad"),
    ("What would you do if a query takes too long?", "Consulta lenta"),
    ("How do you improve database performance?", "Consulta lenta"),
    ("What do you do when you disagree with your manager?", "Desacuerdos"),
    ("How do you handle conflict at work?", "Desacuerdos"),
    ("What interests you about this role?", "Por qué este puesto"),
    ("Why did you apply for this job?", "Por qué este puesto"),
    ("What made you decide to leave your last company?", "Por qué cambiar"),
    ("What's something you're not good at?", "Debilidad"),
    ("What is an area you need to improve?", "Debilidad"),
    ("Can you speak English comfortably?", "Inglés"),
    ("Can you work with a team in the US time zone?", "Zona horaria"),
    ("What compensation are you expecting?", "Salario"),
    ("What's your target salary?", "Salario"),
    ("Do you have anything you'd like to ask?", "Preguntas"),
    ("Before we finish, any questions from your side?", "Preguntas"),
    ("How many years have you worked as a developer?", "Años"),
    ("How long have you been in the industry?", "Años"),
    ("What's the project you're most proud of?", "Proyecto"),
    ("Do you have experience extracting data from PDFs?", "Proyecto"),
    ("Tell me about a time you solved a hard technical problem.", "Problema difícil"),
    ("How soon could you start working with us?", "Disponibilidad"),
    ("Are you available immediately?", "Disponibilidad"),
]

NEGATIVE = [
    "What's the weather like where you live?",
    "Can you hear me okay?",
    "Let me share my screen.",
    "Our company was founded in 2010.",
    "Thanks for joining us today.",
    "Let's move on to the next topic.",
    "My name is Sarah and I'm the engineering manager.",
    "We are a team of fifteen people.",
    "Sorry, I was on mute.",
    "I'll send you the details by email after the call.",
]

m = Matcher(default_path())
cands = [{"id": k, "texts": v} for k, v in BANK.items()]
m.rank("warm up", cands)

t0 = time.perf_counter()
rows = []
for q, expected in POSITIVE:
    r = m.rank(q, cands)
    rows.append((q, expected, r[0]["id"], r[0]["score"], r[0]["score"] - r[1]["score"]))
ms = (time.perf_counter() - t0) / len(POSITIVE) * 1000
neg = []
for q in NEGATIVE:
    r = m.rank(q, cands)
    neg.append((q, r[0]["id"], r[0]["score"]))

ok = sum(1 for _, e, g, *_ in rows if e == g)
print(f"Acierto top-1: {ok}/{len(rows)}  ({100 * ok // len(rows)} %)   ~{ms:.0f} ms por pregunta\n")
for q, e, g, s, margin in rows:
    flag = "  " if e == g else "XX"
    print(f"{flag} {s:.2f} (+{margin:.2f})  {q}\n        esperado={e}  elegido={g}")
print("\n--- Frases que NO deben activar ninguna respuesta ---")
for q, g, s in neg:
    print(f"   {s:.2f}  {q}  → (mejor: {g})")

print("\n--- Umbrales ---")
for t in (0.45, 0.5, 0.55, 0.6, 0.65, 0.7, 0.75):
    right = sum(1 for _, e, g, s, _ in rows if s >= t and e == g)
    wrong = sum(1 for _, e, g, s, _ in rows if s >= t and e != g)
    falsepos = sum(1 for _, _, s in neg if s >= t)
    missed = sum(1 for _, _, _, s, _ in rows if s < t)
    print(f"umbral {t:.2f}: acierta {right}, se equivoca {wrong}, no sugiere {missed} de {len(rows)}; "
          f"falsos positivos en frases irrelevantes {falsepos}/{len(neg)}")
